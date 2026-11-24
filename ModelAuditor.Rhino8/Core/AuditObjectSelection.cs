namespace ModelAuditor.Core;

internal static class AuditObjectSelection
{
    public static IReadOnlyCollection<Guid> FromRows(AuditReport report, IEnumerable<ObjectAuditResult> rows)
    {
        var objects = new HashSet<Guid>();
        var definitions = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (row.BlockDefinitionId is Guid definitionId)
                definitions.Add(definitionId);
            else if (!row.IsDefinitionObject)
                objects.Add(row.ObjectId);
        }

        objects.UnionWith(report.BlockGraph.GetTopLevelObjectIds(definitions));
        return objects;
    }

    public static IReadOnlyCollection<Guid> FromLayers(AuditReport report, IEnumerable<LayerAuditResult> layers)
    {
        var ids = layers.Select(layer => layer.LayerId).ToHashSet();
        return ids.Count == 0
            ? Array.Empty<Guid>()
            : FromRows(report, report.Objects.Where(row => ids.Contains(row.LayerId)));
    }

    public static IReadOnlyCollection<Guid> FromBlocks(AuditReport report, IEnumerable<BlockAuditResult> blocks) =>
        report.BlockGraph.GetTopLevelObjectIds(blocks.Select(block => block.DefinitionId));
}
