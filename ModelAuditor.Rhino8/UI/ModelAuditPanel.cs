using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using ModelAuditor.Core;
using ModelAuditor.Infrastructure;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using IPanel = Rhino.UI.IPanel;
using ShowPanelReason = Rhino.UI.ShowPanelReason;
using PanelIds = Rhino.UI.PanelIds;
using Panels = Rhino.UI.Panels;

namespace ModelAuditor.UI;

[Guid("155F3F31-EF88-45B9-9468-C0C19682511E")]
public sealed class ModelAuditPanel : Panel, IPanel
{
    public static readonly Guid PanelId = new("155F3F31-EF88-45B9-9468-C0C19682511E");
    private static readonly TimeSpan ScanSlice = TimeSpan.FromMilliseconds(12);
    private const int IndividualPreviewLimit = 128;
    private const string ReadyText = "Ctrl/Shift 多选，点击「↗」高亮图层。";

    private readonly Label _status = new() { Text = "等待扫描", Wrap = WrapMode.Character };
    private readonly Label _summary = new() { Text = ReadyText, Wrap = WrapMode.Character };
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 100 };
    private readonly Button _scanButton = CompactButton("一键体检", "快速扫描当前模型", enabled: true);
    private readonly Button _cancelButton = CompactButton("停止", "停止扫描");
    private readonly Button _selectButton = CompactButton("选择", "在 Rhino 中选择或示意对象");
    private readonly Button _zoomButton = CompactButton("定位缩放", "选择或示意对象，并缩放到其范围");
    private readonly Button _clearPreviewButton = CompactButton("清除示意", "关闭视口中的临时轮廓");
    private readonly Button _exportButton = CompactButton("导出 CSV", "导出完整体检报告");

    private readonly DropDown _resultView = new()
    {
        Items = { "重点问题", "物件", "图层", "图块", "属性异常" },
        SelectedIndex = 0,
        ToolTip = "选择体检结果分类"
    };
    private readonly Panel _resultHost = new();
    private readonly AuditGrid<ObjectAuditResult> _issues = new();
    private readonly AuditGrid<ObjectAuditResult> _objects = new();
    private readonly AuditGrid<LayerAuditResult> _layers = new();
    private readonly AuditGrid<BlockAuditResult> _blocks = new();
    private readonly AuditGrid<ObjectAuditResult> _metadata = new();

    private IncrementalDocumentScanner? _scanner;
    private UITimer? _timer;
    private CancellationTokenSource? _finalization;
    private AuditDocumentWatch? _watch;
    private AuditResults? _results;
    private AuditLocationPreview? _preview;
    private uint _previewDocument;
    private long _layerHighlightRequest;

    public ModelAuditPanel()
    {
        BuildGridColumns();
        BuildLayout();
        WireEvents();
    }

    private bool ScanIsRunning => _scanner is not null || _finalization is not null;
    private bool CanUseResults => _results is not null && _watch is { HasChanged: false } &&
        RhinoDoc.ActiveDoc?.RuntimeSerialNumber == _results.Report.DocumentRuntimeSerialNumber;

    private static Button CompactButton(string text, string toolTip, bool enabled = false) => new()
    {
        Text = text,
        ToolTip = toolTip,
        Enabled = enabled,
        MinimumSize = new Size(0, 24)
    };

    private void BuildLayout()
    {
        _scanButton.BackgroundColor = Color.FromArgb(190, 46, 38);
        _scanButton.TextColor = Colors.White;
        var header = new TableLayout
        {
            Padding = new Padding(8, 6, 8, 4),
            Spacing = new Size(6, 4),
            Rows =
            {
                new TableRow(ButtonRow(_scanButton, _cancelButton)),
                new TableRow(_progress),
                new TableRow(_status),
                new TableRow(_summary),
                new TableRow(new TableCell(_resultView, scaleWidth: true), _exportButton)
            }
        };

        _resultHost.Content = _issues;
        var actions = ButtonRow(_selectButton, _zoomButton, _clearPreviewButton);
        actions.Padding = new Padding(8, 4, 8, 6);
        Content = new TableLayout
        {
            Rows = { new TableRow(header), new TableRow(_resultHost) { ScaleHeight = true }, new TableRow(actions) }
        };
    }

    private static TableLayout ButtonRow(params Button[] buttons) => new()
    {
        Spacing = new Size(6, 0),
        Rows =
        {
            new TableRow(buttons.Select(button => new TableCell(button))
                .Append(new TableCell { ScaleWidth = true }))
        }
    };

    private void ChangeResultView()
    {
        ClearPreviewFromUi();
        _resultHost.Content = _resultView.SelectedIndex switch
        {
            1 => _objects,
            2 => _layers,
            3 => _blocks,
            4 => _metadata,
            _ => _issues
        };
    }

    private void WireEvents()
    {
        _scanButton.Click += (_, _) => StartScan();
        _cancelButton.Click += (_, _) => StopScan("扫描已由用户停止。");
        _exportButton.Click += (_, _) => ExportReport();
        _selectButton.Click += (_, _) => LocateCurrent(zoom: false);
        _zoomButton.Click += (_, _) => LocateCurrent(zoom: true);
        _clearPreviewButton.Click += (_, _) => ClearPreviewFromUi();
        _resultView.SelectedIndexChanged += (_, _) => ChangeResultView();
        ClearPreviewOnSelection(_issues);
        ClearPreviewOnSelection(_objects);
        ClearPreviewOnSelection(_layers);
        ClearPreviewOnSelection(_blocks);
        ClearPreviewOnSelection(_metadata);
    }

    private void ClearPreviewOnSelection<T>(AuditGrid<T> grid) where T : class =>
        grid.SelectedRowsChanged += (_, _) =>
        {
            if (!grid.IsReordering)
                ClearPreviewFromUi();
        };

    private void StartScan()
    {
        var document = RhinoDoc.ActiveDoc;
        if (document is null)
        {
            _status.Text = "当前没有活动 Rhino 文档。";
            return;
        }

        CancelWork();
        ClearPreview();
        ReleaseWatch();
        _results = null;
        _issues.SetRows(Array.Empty<ObjectAuditResult>());
        _objects.SetRows(Array.Empty<ObjectAuditResult>());
        _layers.SetRows(Array.Empty<LayerAuditResult>());
        _blocks.SetRows(Array.Empty<BlockAuditResult>());
        _metadata.SetRows(Array.Empty<ObjectAuditResult>());
        _summary.Text = ReadyText;
        _status.Text = "正在读取文档…";
        _progress.Value = 0;
        _progress.Indeterminate = true;

        try
        {
            _watch = new AuditDocumentWatch(document, QueueInvalidation);
            _scanner = new IncrementalDocumentScanner(document);
            _timer = new UITimer { Interval = 0.02 };
            _timer.Elapsed += OnScanTick;
            UpdateButtons();
            _timer.Start();
        }
        catch (Exception ex)
        {
            StopScan($"无法开始扫描：{ex.Message}");
        }
    }

    private void OnScanTick(object? sender, EventArgs e)
    {
        if (_scanner is null || _watch is null)
            return;
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _watch.SerialNumber || _watch.HasChanged)
        {
            InvalidateResults(RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _watch.SerialNumber);
            return;
        }

        try
        {
            _scanner.ProcessSlice(ScanSlice);
            _status.Text = $"正在读取 · 已处理 {_scanner.ProcessedCount:N0} 项";
            if (!_scanner.IsComplete)
                return;

            var (snapshot, savedFilePath) = _scanner.TakeSnapshot();
            StopReading();
            _status.Text = "正在汇总和排序…";
            var cancellation = new CancellationTokenSource();
            _finalization = cancellation;
            _ = FinishScanAsync(snapshot, savedFilePath, cancellation);
        }
        catch (Exception ex)
        {
            StopScan($"扫描失败：{ex.Message}");
        }
    }

    private async Task FinishScanAsync(AuditReport snapshot, string? savedFilePath, CancellationTokenSource cancellation)
    {
        var app = Application.Instance;
        var token = cancellation.Token;
        AuditResults? results = null;
        Exception? failure = null;
        try
        {
            results = await Task.Run(() => AuditResults.Build(snapshot, token, savedFilePath), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        app.AsyncInvoke(() =>
        {
            var isCurrent = ReferenceEquals(_finalization, cancellation);
            cancellation.Dispose();
            if (!isCurrent)
                return;
            _finalization = null;
            if (_watch is null || _watch.HasChanged || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _watch.SerialNumber)
            {
                InvalidateResults(_watch is null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _watch.SerialNumber);
                return;
            }
            if (failure is not null || results is null)
            {
                StopScan(failure is null ? "汇总已停止。" : $"汇总失败：{failure.Message}");
                return;
            }

            _results = results;
            _issues.SetRows(results.Issues);
            _objects.SetRows(results.Objects);
            _layers.SetRows(results.Layers);
            _blocks.SetRows(results.Blocks);
            _metadata.SetRows(results.Metadata);
            _summary.Text = results.Summary;
            _status.Text = results.Status;
            _progress.Indeterminate = false;
            _progress.Value = _progress.MaxValue;
            UpdateButtons();
        });
    }

    private void QueueInvalidation(AuditDocumentWatch watch, bool documentUnavailable) =>
        Application.Instance.AsyncInvoke(() =>
        {
            if (ReferenceEquals(_watch, watch))
                InvalidateResults(documentUnavailable);
        });

    private void InvalidateResults(bool documentUnavailable)
    {
        CancelWork();
        ClearPreview();
        ReleaseWatch();
        _progress.Indeterminate = false;
        _progress.Value = 0;
        _status.Text = documentUnavailable ? "活动文档已切换，请重新扫描。" : "模型内容已变化，请重新扫描。";
        UpdateButtons();
    }

    private void StopScan(string message)
    {
        CancelWork();
        ReleaseWatch();
        _progress.Indeterminate = false;
        _progress.Value = 0;
        _status.Text = message;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _scanButton.Enabled = !ScanIsRunning;
        _cancelButton.Enabled = ScanIsRunning;
        _selectButton.Enabled = _zoomButton.Enabled = _exportButton.Enabled = CanUseResults;
    }

    private void CancelWork()
    {
        StopReading();
        var pending = _finalization;
        _finalization = null;
        pending?.Cancel();
    }

    private void StopReading()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Elapsed -= OnScanTick;
            _timer.Dispose();
            _timer = null;
        }
        _scanner?.Dispose();
        _scanner = null;
    }

    private void ReleaseWatch()
    {
        _watch?.Dispose();
        _watch = null;
    }

    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        UpdateButtons();
    }

    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason)
    {
        if (ScanIsRunning)
            StopScan("面板已隐藏，扫描已停止。");
        ClearPreview();
    }

    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        ClearPreview(redraw: !onCloseDocument);
        StopScan("面板已关闭，请重新扫描。");
        CancelPendingSorts();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelWork();
            ReleaseWatch();
            ClearPreview(redraw: false);
            CancelPendingSorts();
        }
        base.Dispose(disposing);
    }

    private void CancelPendingSorts()
    {
        _issues.CancelPendingSort();
        _objects.CancelPendingSort();
        _layers.CancelPendingSort();
        _blocks.CancelPendingSort();
        _metadata.CancelPendingSort();
    }

    private void LocateCurrent(bool zoom)
    {
        if (!CanUseResults || _results is not { } results || RhinoDoc.ActiveDoc is not { } document)
            return;

        ClearPreview(redraw: false);
        var ids = SelectedObjectIds(results);
        if (ids.Count == 0)
        {
            document.Views.Redraw();
            _status.Text = "所选结果中没有可定位的已放置对象。";
            return;
        }

        var selectable = new List<(Guid Id, BoundingBox Bounds)>();
        var outlines = new List<BoundingBox>();
        var outlineBounds = BoundingBox.Unset;
        var zoomBounds = BoundingBox.Unset;
        var outlineCount = 0;
        var viewport = document.Views.ActiveView?.ActiveViewport;
        var minimumSize = Math.Max(1e-9, document.ModelAbsoluteTolerance);

        void AddOutline(BoundingBox bounds)
        {
            if (!bounds.IsValid || viewport is null)
                return;

            if (bounds.Diagonal.Length < minimumSize)
                bounds.Inflate(minimumSize);
            outlineCount++;
            outlineBounds.Union(bounds);
            zoomBounds.Union(bounds);
            if (outlines.Count < IndividualPreviewLimit)
                outlines.Add(bounds);
        }

        foreach (var id in ids)
        {
            var obj = document.Objects.FindId(id);
            if (obj is null || obj.IsDeleted)
                continue;

            var bounds = results.TopLevelById.GetValueOrDefault(id)?.Bounds ?? BoundingBox.Unset;
            if (!bounds.IsValid)
                bounds = IncrementalDocumentScanner.ReadBounds(obj, obj.Geometry);

            if (obj.IsSelectable(ignoreSelectionState: true, ignoreGripsState: false,
                ignoreLayerLocking: false, ignoreLayerVisibility: false))
                selectable.Add((id, bounds));
            else
                AddOutline(bounds);
        }

        var selected = 0;
        if (selectable.Count > 0)
            document.Objects.UnselectAll();
        foreach (var (id, bounds) in selectable)
        {
            if (document.Objects.Select(id))
            {
                selected++;
                if (bounds.IsValid)
                    zoomBounds.Union(bounds);
            }
            else
            {
                AddOutline(bounds);
            }
        }

        var combinedOutline = outlineCount > IndividualPreviewLimit;
        if (outlineCount > 0 && viewport is not null)
        {
            _previewDocument = document.RuntimeSerialNumber;
            _preview = new AuditLocationPreview(document, viewport,
                combinedOutline ? new[] { outlineBounds } : outlines, outlineBounds);
            _preview.Enabled = true;
            _clearPreviewButton.Enabled = true;
        }

        var message = $"已选择 {selected:N0} · 已示意 {outlineCount:N0}";
        if (combinedOutline)
            message += "（合并范围）";
        var unavailable = ids.Count - selected - outlineCount;
        if (unavailable > 0)
            message += $" · 无法定位 {unavailable:N0}";

        if (zoom && viewport is not null && zoomBounds.IsValid)
        {
            if (zoomBounds.Diagonal.Length < minimumSize)
                zoomBounds.Inflate(minimumSize);
            if (!viewport.ZoomBoundingBox(zoomBounds))
                message += " · 当前视口无法缩放到该范围";
        }
        else if (zoom && selected > 0 && viewport is not null)
        {
            RhinoApp.RunScript("_Zoom _Selected", false);
        }
        document.Views.Redraw();
        _status.Text = message;
    }

    private void ClearPreviewFromUi()
    {
        if (_preview is null)
            return;

        ClearPreview();
        _status.Text = "已清除临时示意。";
    }

    private void ClearPreview(bool redraw = true)
    {
        if (_preview is null)
            return;

        _preview.Enabled = false;
        _preview = null;
        _clearPreviewButton.Enabled = false;
        if (redraw)
            RhinoDoc.FromRuntimeSerialNumber(_previewDocument)?.Views.Redraw();
    }

    private IReadOnlyCollection<Guid> SelectedObjectIds(AuditResults results) => _resultView.SelectedIndex switch
    {
        0 => AuditObjectSelection.FromRows(results.Report, _issues.SelectedItems),
        1 => AuditObjectSelection.FromRows(results.Report, _objects.SelectedItems),
        2 => AuditObjectSelection.FromLayers(results.Report, _layers.SelectedItems),
        3 => AuditObjectSelection.FromBlocks(results.Report, _blocks.SelectedItems),
        4 => AuditObjectSelection.FromRows(results.Report, _metadata.SelectedItems),
        _ => Array.Empty<Guid>()
    };

    private void ExportReport()
    {
        if (!CanUseResults || _results is not { } results)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "导出模型体检报告",
            FileName = $"ModelAudit_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        dialog.Filters.Add(new FileFilter("CSV 文件", ".csv"));
        if (dialog.ShowDialog(this) != DialogResult.Ok || !CanUseResults || !ReferenceEquals(_results, results))
            return;

        try
        {
            CsvAuditExporter.Export(dialog.FileName, results.Report);
            _status.Text = $"报告已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导出失败：{ex.Message}", "模型体检", MessageBoxType.Error);
        }
    }

    private void AddLayerLink(AuditGrid<ObjectAuditResult> grid)
    {
        var column = grid.AddColumn("图层 ↗", 120, row => row.LayerPath);
        WireLayerLink(grid, column, row => row.LayerId);
    }

    private void WireLayerLink<T>(AuditGrid<T> grid, GridColumn column, Func<T, Guid> layerId) where T : class
    {
        grid.CellClick += (_, e) =>
        {
            if (e.GridColumn != column || !e.Buttons.HasFlag(MouseButtons.Primary) || e.Modifiers != Keys.None ||
                e.Item is not T row || !CanUseResults || _results is not { } results)
                return;

            var id = layerId(row);
            if (id == Guid.Empty)
                return;

            e.Handled = true;
            var request = ++_layerHighlightRequest;
            Application.Instance.AsyncInvoke(() => HighlightRhinoLayer(results, id, request));
        };
    }

    private void HighlightRhinoLayer(AuditResults results, Guid layerId, long request, int attempt = 0)
    {
        if (request != _layerHighlightRequest || !ReferenceEquals(_results, results) ||
            !CanUseResults || RhinoDoc.ActiveDoc is not { } document)
            return;

        var layer = document.Layers.FindId(layerId);
        if (layer is null || layer.IsDeleted)
        {
            _status.Text = "该图层已不可用，请重新扫描。";
            return;
        }

        if (attempt == 0)
        {
            Panels.OpenPanel(PanelIds.Layers, makeSelectedPanel: true);
            if (!CanUseResults)
                return;

            ExpandLayerAncestors(document, layer);
            _ = HighlightAfterLayerRefreshAsync(results, layerId, request, attempt: 1);
            return;
        }

        var index = layer.Index;
        if (document.Layers.Select(new[] { index }, bDeselect: true) &&
            document.Layers.GetSelected(out var selectedLayers) && selectedLayers.Contains(index))
        {
            _status.Text = $"已在 Rhino 图层面板高亮 {layer.FullPath}。";
        }
        else if (attempt == 1)
        {
            _ = HighlightAfterLayerRefreshAsync(results, layerId, request, attempt: 2);
        }
        else
        {
            _status.Text = "当前无法在 Rhino 图层面板中选中该图层。";
        }
    }

    private async Task HighlightAfterLayerRefreshAsync(AuditResults results, Guid layerId, long request, int attempt)
    {
        // Layer updates are picked up by the native tree on its next UI refresh.
        var app = Application.Instance;
        await Task.Delay(50).ConfigureAwait(false);
        app.AsyncInvoke(() => HighlightRhinoLayer(results, layerId, request, attempt));
    }

    private static void ExpandLayerAncestors(RhinoDoc document, Layer target)
    {
        var ancestors = new Stack<Guid>();
        var seen = new HashSet<Guid> { target.Id };
        var parentId = target.ParentLayerId;
        while (parentId != Guid.Empty && seen.Add(parentId))
        {
            var parent = document.Layers.FindId(parentId);
            if (parent is null || parent.IsDeleted)
                break;

            if (!parent.IsExpanded)
                ancestors.Push(parent.Id);
            parentId = parent.ParentLayerId;
        }

        while (ancestors.TryPop(out var id))
        {
            if (document.Layers.FindId(id) is { IsDeleted: false, IsExpanded: false } parent)
                parent.IsExpanded = true;
        }
    }

    private void BuildGridColumns()
    {
        _issues.AddColumn("风险", 55, x => x.Severity, AuditScoring.SeverityText);
        _issues.AddColumn("名称", 96, x => x.Name);
        AddLayerLink(_issues);
        _issues.AddColumn("类型", 80, x => x.GeometryType);
        _issues.AddColumn("小而重", 65, x => x.SmallHeavyScore, value => value.ToString("0.0"));
        _issues.AddColumn("诊断", 355, x => string.Join("；", x.Findings));

        _objects.AddColumn("名称", 104, x => x.Name);
        AddLayerLink(_objects);
        _objects.AddColumn("类型", 80, x => x.GeometryType);
        _objects.AddColumn("估算内存", 90, x => x.MemoryBytes, AuditScoring.FormatBytes);
        _objects.AddColumn("显示复杂度", 105, x => x.DisplayComplexity, value => value.ToString("N0"));
        _objects.AddColumn("小而重", 65, x => x.SmallHeavyScore, value => value.ToString("0.0"));
        _objects.AddColumn("风险", 55, x => x.Severity, AuditScoring.SeverityText);

        _layers.AddColumn("图层", 140, x => x.LayerPath);
        var layerLink = _layers.AddColumn("高亮", 52, x => x.LayerId == Guid.Empty ? "" : "↗");
        layerLink.Sortable = false;
        WireLayerLink(_layers, layerLink, x => x.LayerId);
        _layers.AddColumn("物件数", 75, x => x.ObjectCount, value => value.ToString("N0"));
        _layers.AddColumn("估算内存", 95, x => x.MemoryBytes, AuditScoring.FormatBytes);
        _layers.AddColumn("属性文本", 90, x => x.MetadataBytes, AuditScoring.FormatBytes);
        _layers.AddColumn("顶层显示复杂度", 120, x => x.DisplayComplexity, value => value.ToString("N0"));
        _layers.AddColumn("风险", 55, x => x.Severity, AuditScoring.SeverityText);

        _blocks.AddColumn("块定义", 170, x => x.Name);
        _blocks.AddColumn("定义物件", 75, x => x.DefinitionObjectCount, value => value.ToString("N0"));
        _blocks.AddColumn("顶层实例", 75, x => x.DirectInstanceCount, value => value.ToString("N0"));
        _blocks.AddColumn("实例总数", 75, x => x.DocumentInstanceCount, value => value.ToString("N0"));
        _blocks.AddColumn("定义内存估算", 110, x => x.DefinitionMemoryBytes, AuditScoring.FormatBytes);
        _blocks.AddColumn("自身累计负载", 110, x => x.ExpandedDisplayComplexity, value => value.ToString("N0"));
        _blocks.AddColumn("单实例含子块", 110, x => x.InstanceDisplayComplexity, value => value.ToString("N0"));
        _blocks.AddColumn("风险", 55, x => x.Severity, AuditScoring.SeverityText);

        _metadata.AddColumn("名称", 104, x => x.Name);
        AddLayerLink(_metadata);
        _metadata.AddColumn("来源", 90, x => x.IsDefinitionObject ? "块内物件" : x.GeometryType);
        _metadata.AddColumn("文本估算", 90, x => x.MetadataBytes, AuditScoring.FormatBytes);
        _metadata.AddColumn("键值数", 65, x => x.UserStringCount, value => value.ToString("N0"));
        _metadata.AddColumn("自定义数据", 85, x => x.CustomUserDataCount, value => value.ToString("N0"));
        _metadata.AddColumn("受限预览", 220, x => x.MetadataPreview);
        _metadata.AddColumn("风险", 55, x => x.Severity, AuditScoring.SeverityText);
    }
}
