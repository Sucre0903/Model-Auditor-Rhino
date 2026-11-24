using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using ModelAuditor.Core;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace ModelAuditor.Infrastructure;

internal sealed class IncrementalDocumentScanner : IDisposable
{
    private readonly RhinoDoc _document;
    private readonly IEnumerator<ScanItem> _items;
    private readonly Dictionary<int, (Guid Id, string Path)> _layers = new();
    private readonly AuditReport _report;
    private readonly string? _savedFilePath;
    private BoundingBox _modelBounds = BoundingBox.Unset;

    public IncrementalDocumentScanner(RhinoDoc document)
    {
        _document = document;
        _savedFilePath = document.Path;
        _report = new AuditReport
        {
            DocumentRuntimeSerialNumber = document.RuntimeSerialNumber,
            DocumentName = string.IsNullOrWhiteSpace(document.Name) ? "未保存文件" : document.Name
        };
        _items = EnumerateItems().GetEnumerator();
    }

    public int ProcessedCount => _report.Objects.Count;
    public bool IsComplete { get; private set; }

    public void ProcessSlice(TimeSpan budget)
    {
        if (IsComplete)
            return;

        var start = Stopwatch.GetTimestamp();
        do
        {
            if (!_items.MoveNext())
            {
                IsComplete = true;
                _report.ModelBounds = _modelBounds;
                return;
            }

            var item = _items.Current;
            // An empty item yields after visiting a definition or skipping a Rhino row.
            if (item.Object is null)
                continue;

            var result = ScanObject(item.Object, item.DefinitionId);
            _report.Objects.Add(result);
            if (!result.IsDefinitionObject && result.Bounds.IsValid)
                _modelBounds.Union(result.Bounds);
        }
        while (Stopwatch.GetElapsedTime(start) < budget);
    }

    public (AuditReport Report, string? SavedFilePath) TakeSnapshot()
    {
        if (!IsComplete)
            throw new InvalidOperationException("扫描尚未完成。");

        return (_report, _savedFilePath);
    }

    public void Dispose() => _items.Dispose();

    private IEnumerable<ScanItem> EnumerateItems()
    {
        var definitionObjectIds = new HashSet<Guid>();
        foreach (var definition in _document.InstanceDefinitions)
        {
            if (definition is null || definition.IsDeleted)
            {
                yield return default;
                continue;
            }

            var definitionId = definition.Id;
            _report.BlockGraph.AddDefinition(new BlockAuditResult
            {
                DefinitionId = definitionId,
                Name = string.IsNullOrWhiteSpace(definition.Name)
                    ? $"未命名块 {definition.Index}"
                    : definition.Name
            });
            yield return default;

            foreach (var obj in definition.GetObjects() ?? Array.Empty<RhinoObject>())
            {
                yield return obj is not null && definitionObjectIds.Add(obj.Id)
                    ? new ScanItem(obj, definitionId)
                    : default;
            }
        }

        var settings = new ObjectEnumeratorSettings
        {
            ActiveObjects = true,
            NormalObjects = true,
            HiddenObjects = true,
            LockedObjects = true,
            DeletedObjects = false,
            IdefObjects = false
        };
        foreach (var obj in _document.Objects.GetObjectList(settings))
        {
            yield return obj is not null && !definitionObjectIds.Contains(obj.Id)
                ? new ScanItem(obj, null)
                : default;
        }
    }

    private ObjectAuditResult ScanObject(RhinoObject obj, Guid? definitionId)
    {
        var geometry = obj.Geometry;
        var reference = geometry as InstanceReferenceGeometry;
        var memoryBytes = SafeMemoryEstimate(obj, geometry);
        var (layerId, layerPath) = FindLayer(obj.Attributes.LayerIndex);
        var bounds = definitionId.HasValue ? BoundingBox.Unset : ReadBounds(obj, geometry);

        var metadata = MeasureMetadata(obj, geometry);
        var meshVertices = 0;
        var meshFaces = 0;
        long renderVertices = 0;
        long renderFaces = 0;
        var brepFaces = 0;
        var brepEdges = 0;
        var brepTrims = 0;
        var curveSpans = 0;
        var curvePoints = 0;
        var pointCount = 0;

        switch (geometry)
        {
            case Mesh mesh:
                meshVertices = mesh.Vertices.Count;
                meshFaces = mesh.Faces.Count;
                break;
            case Brep brep:
                brepFaces = brep.Faces.Count;
                brepEdges = brep.Edges.Count;
                brepTrims = brep.Trims.Count;
                break;
            case NurbsCurve nurbs:
                curveSpans = nurbs.SpanCount;
                curvePoints = nurbs.Points.Count;
                break;
            case Curve curve:
                curveSpans = curve.SpanCount;
                break;
            case PointCloud cloud:
                pointCount = cloud.Count;
                break;
            case SubD subd:
                meshVertices = subd.Vertices.Count;
                meshFaces = subd.Faces.Count;
                break;
        }

        try
        {
            // Instance geometry is accounted for through its definition graph.
            var renderMeshes = reference is null ? obj.GetMeshes(MeshType.Render) : null;
            if (renderMeshes is not null)
            {
                foreach (var mesh in renderMeshes)
                {
                    if (mesh is null)
                        continue;
                    renderVertices += mesh.Vertices.Count;
                    renderFaces += mesh.Faces.Count;
                }
            }
        }
        catch
        {
            // Fast scan reads existing caches only. Unsupported custom objects are skipped.
        }

        var displayComplexity =
            renderFaces > 0
                ? renderFaces
                : meshFaces +
                  brepFaces * 64.0 +
                  brepEdges * 4.0 +
                  curveSpans * 8.0 +
                  pointCount;

        return new ObjectAuditResult
        {
            ObjectId = obj.Id,
            BlockDefinitionId = definitionId,
            ReferencedDefinitionId = reference?.ParentIdefId,
            IsDefinitionObject = definitionId.HasValue,
            Name = string.IsNullOrWhiteSpace(obj.Attributes.Name)
                ? $"[{obj.Id.ToString()[..8]}]"
                : obj.Attributes.Name,
            LayerId = layerId,
            LayerPath = layerPath,
            GeometryType = obj.ObjectType.ToString(),
            MemoryBytes = memoryBytes,
            MetadataBytes = metadata.Bytes,
            UserStringCount = metadata.UserStringCount,
            CustomUserDataCount = metadata.CustomUserDataCount,
            MetadataPreview = metadata.Preview,
            MeshVertices = meshVertices,
            MeshFaces = meshFaces,
            RenderMeshVertices = renderVertices,
            RenderMeshFaces = renderFaces,
            BrepFaces = brepFaces,
            BrepEdges = brepEdges,
            BrepTrims = brepTrims,
            CurveSpans = curveSpans,
            CurveControlPoints = curvePoints,
            PointCount = pointCount,
            Bounds = bounds,
            DisplayComplexity = displayComplexity
        };
    }

    private (Guid Id, string Path) FindLayer(int layerIndex)
    {
        if (_layers.TryGetValue(layerIndex, out var entry))
            return entry;

        entry = (Guid.Empty, "(无有效图层)");
        if (layerIndex >= 0 && layerIndex < _document.Layers.Count)
        {
            var layer = _document.Layers[layerIndex];
            if (layer is not null && !layer.IsDeleted)
                entry = (layer.Id, string.IsNullOrWhiteSpace(layer.FullPath) ? layer.Name : layer.FullPath);
        }
        _layers.Add(layerIndex, entry);
        return entry;
    }

    private static long SafeMemoryEstimate(RhinoObject obj, GeometryBase? geometry)
    {
        try
        {
            // Reference geometry stores the instance itself. Its definition is scanned separately.
            return geometry is InstanceReferenceGeometry
                ? Convert.ToInt64(geometry.MemoryEstimate())
                : Convert.ToInt64(obj.MemoryEstimate());
        }
        catch
        {
            try
            {
                return geometry is null ? 0L : Convert.ToInt64(geometry.MemoryEstimate());
            }
            catch
            {
                return 0L;
            }
        }
    }

    internal static BoundingBox ReadBounds(RhinoObject obj, GeometryBase? geometry)
    {
        if (geometry is null)
            return BoundingBox.Unset;
        try
        {
            var bounds = geometry.GetBoundingBox(false);
            if (bounds.IsValid || geometry is not InstanceReferenceGeometry)
                return bounds;

            return RhinoObject.GetTightBoundingBox(new[] { obj }, out var tightBounds)
                ? tightBounds
                : BoundingBox.Unset;
        }
        catch
        {
            return BoundingBox.Unset;
        }
    }

    private static MetadataMeasure MeasureMetadata(RhinoObject obj, GeometryBase? geometry)
    {
        long bytes = 0;
        var largestBytes = 0;
        var count = 0;
        var preview = "";

        AddText("名称", obj.Attributes.Name, ref bytes, ref largestBytes, ref preview);
        MeasureStrings(
            "对象属性",
            obj.Attributes.GetUserStrings(),
            ref bytes,
            ref largestBytes,
            ref count,
            ref preview);

        if (geometry is not null)
        {
            MeasureStrings(
                "几何属性",
                geometry.GetUserStrings(),
                ref bytes,
                ref largestBytes,
                ref count,
                ref preview);

            if (geometry is TextEntity text)
                AddText("文字", text.PlainText, ref bytes, ref largestBytes, ref preview);
            else if (geometry is TextDot dot)
                AddText("文字点", dot.Text, ref bytes, ref largestBytes, ref preview);
        }

        var customDataCount = obj.Attributes.UserData.Count + (geometry?.UserData.Count ?? 0);
        return new MetadataMeasure(bytes, count, customDataCount, preview);
    }

    private static void MeasureStrings(
        string source,
        NameValueCollection? values,
        ref long bytes,
        ref int largestBytes,
        ref int count,
        ref string preview)
    {
        if (values is null)
            return;

        foreach (var key in values.AllKeys)
        {
            if (key is null)
                continue;
            count++;
            AddText($"{source}键", key, ref bytes, ref largestBytes, ref preview);
            AddText(
                $"{source} {key}",
                values[key],
                ref bytes,
                ref largestBytes,
                ref preview);
        }
    }

    private static void AddText(
        string source,
        string? value,
        ref long bytes,
        ref int largestBytes,
        ref string preview)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var valueBytes = Encoding.UTF8.GetByteCount(value);
        bytes += valueBytes;
        if (valueBytes > largestBytes)
        {
            largestBytes = valueBytes;
            var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            var sample = normalized.Length <= 48 ? normalized : normalized[..48] + "…";
            preview = $"{source}：{sample}";
        }
    }

    private readonly record struct ScanItem(RhinoObject? Object, Guid? DefinitionId);

    private sealed record MetadataMeasure(
        long Bytes,
        int UserStringCount,
        int CustomUserDataCount,
        string Preview);
}
