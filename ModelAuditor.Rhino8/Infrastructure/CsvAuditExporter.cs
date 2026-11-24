using System.Globalization;
using System.Text;
using ModelAuditor.Core;

namespace ModelAuditor.Infrastructure;

public static class CsvAuditExporter
{
    public static void Export(string path, AuditReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine($"文件,{Csv(report.DocumentName)},已保存文件字节,{report.FileSizeBytes?.ToString(culture) ?? ""}");
        writer.WriteLine($"扫描时间,{Csv(report.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss", culture))}");
        writer.WriteLine($"估算内存字节,{report.TotalMemoryBytes},顶层显示复杂度,{report.TotalDisplayComplexity.ToString("0", culture)}");
        writer.WriteLine($"缺失定义引用,{report.MissingDefinitionReferences},跳过的循环引用,{report.CyclicReferences},实例数达到上限,{(report.InstanceCountsCapped ? "是" : "否")}");
        writer.WriteLine();
        writer.WriteLine("分类,名称,类型/图层,估算内存字节,属性文本字节,显示复杂度,小而重评分,风险,问题,定义物件数,顶层实例数,实例总数,单实例含子块负载,属性键值数,自定义数据数");

        foreach (var item in report.Objects
                     .OrderByDescending(x => x.Severity)
                     .ThenByDescending(x => x.SmallHeavyScore)
                     .ThenByDescending(x => x.MemoryBytes))
        {
            writer.WriteLine(string.Join(",",
                Csv(item.IsDefinitionObject ? "块定义物件" : "普通物件"),
                Csv(item.Name),
                Csv($"{item.GeometryType} / {item.LayerPath}"),
                item.MemoryBytes,
                item.MetadataBytes,
                item.DisplayComplexity.ToString("0", culture),
                item.SmallHeavyScore.ToString("0.0", culture),
                Csv(AuditScoring.SeverityText(item.Severity)),
                Csv(string.Join("；", item.Findings)),
                "", "", "", "",
                item.UserStringCount,
                item.CustomUserDataCount));
        }

        foreach (var block in report.Blocks.OrderByDescending(x => x.ExpandedDisplayComplexity))
        {
            writer.WriteLine(string.Join(",",
                Csv("块定义"),
                Csv(block.Name),
                "",
                block.DefinitionMemoryBytes,
                "",
                block.ExpandedDisplayComplexity.ToString("0", culture),
                "",
                Csv(AuditScoring.SeverityText(block.Severity)),
                Csv("自身累计负载 = 本定义直属几何复杂度 × 模型中的实例总数"),
                block.DefinitionObjectCount,
                block.DirectInstanceCount,
                block.DocumentInstanceCount,
                block.InstanceDisplayComplexity.ToString("0", culture),
                "", ""));
        }
    }

    private static string Csv(string? value)
    {
        var text = value ?? "";
        var trimmed = text.AsSpan().TrimStart();
        if ((!trimmed.IsEmpty && trimmed[0] is '=' or '+' or '-' or '@') ||
            (text.Length > 0 && text[0] is '\t' or '\r' or '\n'))
            text = "'" + text;

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
