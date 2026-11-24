namespace ModelAuditor.Core;

public static class AuditScoring
{
    public static void FinalizeReport(AuditReport report, CancellationToken cancellation = default)
    {
        var modelDiagonal = report.ModelBounds.IsValid
            ? report.ModelBounds.Diagonal.Length
            : 0.0;

        foreach (var item in report.Objects)
        {
            cancellation.ThrowIfCancellationRequested();
            item.SmallHeavyScore = SmallHeavyScore(item, modelDiagonal);

            if (item.SmallHeavyScore >= 60.0 && item.DisplayComplexity >= 100_000)
                item.Findings.Add($"小而重 · {item.SmallHeavyScore:0.0} 分，显示复杂度 {item.DisplayComplexity:N0}");

            if (item.MetadataBytes >= 1_000_000)
                item.Findings.Add($"属性文本危险 · {item.MetadataBytes:N0} 字节");
            else if (item.MetadataBytes >= 100_000)
                item.Findings.Add($"属性文本严重 · {item.MetadataBytes:N0} 字节");
            else if (item.MetadataBytes >= 10_000)
                item.Findings.Add($"属性文本偏长 · {item.MetadataBytes:N0} 字节");

            if (item.RenderMeshFaces >= 1_000_000)
                item.Findings.Add($"已有渲染网格 · {item.RenderMeshFaces:N0} 面");
            if (item.BrepFaces >= 10_000)
                item.Findings.Add($"Brep 面数偏高 · {item.BrepFaces:N0} 面");
            if (item.PointCount >= 1_000_000)
                item.Findings.Add($"点数量偏高 · {item.PointCount:N0} 点");
            if (item.DisplayComplexity >= 100_000 && item.RenderMeshFaces < 1_000_000 &&
                item.BrepFaces < 10_000 && item.PointCount < 1_000_000 && item.SmallHeavyScore < 60.0)
            {
                item.Findings.Add(item.ReferencedDefinitionId.HasValue
                    ? $"图块展开复杂度估算偏高 · {item.DisplayComplexity:N0}"
                    : $"显示复杂度偏高 · {item.DisplayComplexity:N0}");
            }

            item.Severity = SeverityFrom(item);
        }

        foreach (var layer in report.Layers)
        {
            cancellation.ThrowIfCancellationRequested();
            layer.Severity = layer.DisplayComplexity >= 10_000_000 || layer.MemoryBytes >= 500_000_000
                ? AuditSeverity.Danger
                : layer.DisplayComplexity >= 1_000_000 || layer.MemoryBytes >= 100_000_000
                    ? AuditSeverity.Serious
                    : layer.DisplayComplexity >= 100_000 || layer.MemoryBytes >= 10_000_000
                        ? AuditSeverity.Notice
                        : AuditSeverity.Healthy;
        }

        foreach (var block in report.Blocks)
        {
            cancellation.ThrowIfCancellationRequested();
            block.Severity = block.ExpandedDisplayComplexity >= 10_000_000
                ? AuditSeverity.Danger
                : block.ExpandedDisplayComplexity >= 1_000_000
                    ? AuditSeverity.Serious
                    : block.ExpandedDisplayComplexity >= 100_000
                        ? AuditSeverity.Notice
                        : AuditSeverity.Healthy;
        }
    }

    private static double SmallHeavyScore(ObjectAuditResult item, double modelDiagonal)
    {
        // Only placed objects have a size in the document coordinate system.
        if (item.IsDefinitionObject || !item.Bounds.IsValid || modelDiagonal <= 0 ||
            !double.IsFinite(modelDiagonal) || item.DisplayComplexity < 100_000)
            return 0;

        var itemDiagonal = item.Bounds.Diagonal.Length;
        if (!double.IsFinite(itemDiagonal))
            return 0;

        var relativeSize = itemDiagonal / modelDiagonal;
        var smallness = Math.Clamp(1.0 - relativeSize * 100.0, 0.0, 1.0);
        var load = Math.Min(Math.Log10(1.0 + item.DisplayComplexity) / 7.0, 1.0);
        return Math.Round(100.0 * smallness * load, 1);
    }

    private static AuditSeverity SeverityFrom(ObjectAuditResult item)
    {
        if (item.MetadataBytes >= 1_000_000 ||
            item.RenderMeshFaces >= 1_000_000 ||
            item.SmallHeavyScore >= 75.0)
            return AuditSeverity.Danger;

        if (item.MetadataBytes >= 100_000 ||
            item.DisplayComplexity >= 1_000_000 ||
            item.SmallHeavyScore >= 60.0)
            return AuditSeverity.Serious;

        if (item.Findings.Count > 0 || item.DisplayComplexity >= 100_000)
            return AuditSeverity.Notice;

        return AuditSeverity.Healthy;
    }

    public static string SeverityText(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Danger => "危险",
        AuditSeverity.Serious => "严重",
        AuditSeverity.Notice => "提醒",
        _ => "正常"
    };

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30)
            return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20)
            return $"{bytes / (double)(1L << 20):0.00} MB";
        if (bytes >= 1L << 10)
            return $"{bytes / (double)(1L << 10):0.0} KB";
        return $"{bytes} B";
    }
}
