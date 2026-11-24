namespace ModelAuditor.Core;

internal sealed record AuditResults(
    AuditReport Report,
    ObjectAuditResult[] Issues,
    ObjectAuditResult[] Objects,
    LayerAuditResult[] Layers,
    BlockAuditResult[] Blocks,
    ObjectAuditResult[] Metadata,
    IReadOnlyDictionary<Guid, ObjectAuditResult> TopLevelById,
    string Summary,
    string Status)
{
    public static AuditResults Build(AuditReport report, CancellationToken cancellation, string? savedFilePath = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(savedFilePath))
            report.FileSizeBytes = ReadSavedFileSize(savedFilePath);

        var graph = report.BlockGraph;
        foreach (var item in report.Objects)
        {
            cancellation.ThrowIfCancellationRequested();
            if (item.ReferencedDefinitionId is Guid referencedId)
                graph.AddReference(item.ObjectId, item.BlockDefinitionId, referencedId);

            if (item.BlockDefinitionId is Guid ownerId && graph.FindDefinition(ownerId) is { } block)
            {
                block.DefinitionObjectCount++;
                block.DefinitionMemoryBytes += item.MemoryBytes;
                block.DefinitionDisplayComplexity += item.DisplayComplexity;
            }
        }

        graph.Calculate(cancellation);
        var layers = new Dictionary<Guid, LayerAuditResult>();
        var topLevel = new Dictionary<Guid, ObjectAuditResult>();
        long memoryBytes = 0;
        long metadataBytes = 0;
        foreach (var item in report.Objects)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!item.IsDefinitionObject)
            {
                topLevel[item.ObjectId] = item;
                if (item.ReferencedDefinitionId is Guid definitionId)
                    item.DisplayComplexity = graph.FindDefinition(definitionId)?.InstanceDisplayComplexity ?? 0;
            }

            if (!layers.TryGetValue(item.LayerId, out var layer))
            {
                layer = new LayerAuditResult { LayerId = item.LayerId, LayerPath = item.LayerPath };
                layers.Add(item.LayerId, layer);
            }
            layer.ObjectCount++;
            layer.MemoryBytes += item.MemoryBytes;
            layer.MetadataBytes += item.MetadataBytes;
            if (!item.IsDefinitionObject)
                layer.DisplayComplexity = Math.Min(double.MaxValue, layer.DisplayComplexity + item.DisplayComplexity);

            memoryBytes += item.MemoryBytes;
            metadataBytes += item.MetadataBytes;
        }

        report.Layers.AddRange(layers.Values);
        AuditScoring.FinalizeReport(report, cancellation);

        var issues = report.Objects.Where(x => x.Findings.Count > 0).OrderByDescending(x => x.Severity)
            .ThenByDescending(x => x.SmallHeavyScore).ThenByDescending(x => x.DisplayComplexity).ToArray();
        cancellation.ThrowIfCancellationRequested();
        var objects = topLevel.Values.OrderByDescending(x => x.Severity).ThenByDescending(x => x.MemoryBytes).ToArray();
        var layerRows = report.Layers.OrderByDescending(x => x.Severity).ThenByDescending(x => x.MemoryBytes).ToArray();
        var blocks = report.Blocks.OrderByDescending(x => x.Severity).ThenByDescending(x => x.ExpandedDisplayComplexity).ToArray();
        cancellation.ThrowIfCancellationRequested();
        var metadata = report.Objects.Where(x => x.MetadataBytes > 0 || x.CustomUserDataCount > 0)
            .OrderByDescending(x => x.MetadataBytes).ToArray();
        cancellation.ThrowIfCancellationRequested();

        var diskSize = report.FileSizeBytes is long bytes ? AuditScoring.FormatBytes(bytes) : "不可用";
        var summary = $"磁盘文件 {diskSize}　估算内存 {AuditScoring.FormatBytes(memoryBytes)}\n" +
            $"顶层物件 {objects.Length:N0}　块内物件 {report.Objects.Count - objects.Length:N0}　" +
            $"属性文本 {AuditScoring.FormatBytes(metadataBytes)}\n显示复杂度来自已有网格与拓扑估算。";
        var status = $"扫描完成 · {report.Objects.Count:N0} 项";
        if (report.MissingDefinitionReferences > 0 || report.CyclicReferences > 0)
        {
            status += $" · 跳过 {report.MissingDefinitionReferences:N0} 个缺失定义引用、" +
                $"{report.CyclicReferences:N0} 个循环引用";
        }
        if (report.InstanceCountsCapped)
            status += " · 部分实例数已达统计上限";

        return new AuditResults(report, issues, objects, layerRows, blocks, metadata, topLevel, summary, status);
    }

    private static long? ReadSavedFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
