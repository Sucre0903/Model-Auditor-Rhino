using Rhino.Geometry;

namespace ModelAuditor.Core;

public enum AuditSeverity
{
    Healthy = 0,
    Notice = 1,
    Serious = 2,
    Danger = 3
}

public sealed class ObjectAuditResult
{
    public Guid ObjectId { get; init; }
    public Guid? BlockDefinitionId { get; init; }
    public Guid? ReferencedDefinitionId { get; init; }
    public bool IsDefinitionObject { get; init; }
    public string Name { get; init; } = "";
    public Guid LayerId { get; init; }
    public string LayerPath { get; init; } = "";
    public string GeometryType { get; init; } = "";
    public long MemoryBytes { get; init; }
    public long MetadataBytes { get; init; }
    public int UserStringCount { get; init; }
    public int CustomUserDataCount { get; init; }
    public string MetadataPreview { get; init; } = "";
    public int MeshVertices { get; init; }
    public int MeshFaces { get; init; }
    public long RenderMeshVertices { get; init; }
    public long RenderMeshFaces { get; init; }
    public int BrepFaces { get; init; }
    public int BrepEdges { get; init; }
    public int BrepTrims { get; init; }
    public int CurveSpans { get; init; }
    public int CurveControlPoints { get; init; }
    public int PointCount { get; init; }
    public BoundingBox Bounds { get; init; }
    public double DisplayComplexity { get; internal set; }
    public double SmallHeavyScore { get; set; }
    public AuditSeverity Severity { get; set; }
    public List<string> Findings { get; } = new();
}

public sealed class LayerAuditResult
{
    public Guid LayerId { get; init; }
    public string LayerPath { get; init; } = "";
    public int ObjectCount { get; set; }
    public long MemoryBytes { get; set; }
    public long MetadataBytes { get; set; }
    public double DisplayComplexity { get; set; }
    public AuditSeverity Severity { get; set; }
}

public sealed class BlockAuditResult
{
    public Guid DefinitionId { get; init; }
    public string Name { get; init; } = "";
    public int DefinitionObjectCount { get; set; }
    public int DirectInstanceCount { get; set; }
    public long DocumentInstanceCount { get; set; }
    public long DefinitionMemoryBytes { get; set; }
    public double DefinitionDisplayComplexity { get; set; }
    public double InstanceDisplayComplexity { get; set; }
    // Only geometry owned by this definition contributes here, so these rows are additive.
    public double ExpandedDisplayComplexity =>
        Math.Min(double.MaxValue, DefinitionDisplayComplexity * DocumentInstanceCount);
    public AuditSeverity Severity { get; set; }
}

public sealed class AuditReport
{
    public uint DocumentRuntimeSerialNumber { get; init; }
    public string DocumentName { get; init; } = "";
    public long? FileSizeBytes { get; internal set; }
    public DateTime ScannedAt { get; init; } = DateTime.Now;
    public BoundingBox ModelBounds { get; set; }
    public List<ObjectAuditResult> Objects { get; } = new();
    public List<LayerAuditResult> Layers { get; } = new();
    internal BlockAuditGraph BlockGraph { get; } = new();
    public IReadOnlyCollection<BlockAuditResult> Blocks => BlockGraph.Blocks;
    public long MissingDefinitionReferences => BlockGraph.MissingDefinitionReferences;
    public long CyclicReferences => BlockGraph.CyclicReferences;
    public bool InstanceCountsCapped => BlockGraph.InstanceCountsCapped;

    public IReadOnlyList<Guid> GetTopLevelObjectIds(Guid definitionId) =>
        BlockGraph.GetTopLevelObjectIds(definitionId);

    public long TotalMemoryBytes => Objects.Sum(x => x.MemoryBytes);
    public long TotalMetadataBytes => Objects.Sum(x => x.MetadataBytes);
    public double TotalDisplayComplexity => Math.Min(double.MaxValue,
        Objects.Where(x => !x.IsDefinitionObject).Sum(x => x.DisplayComplexity));
    public int NormalObjectCount => Objects.Count(x => !x.IsDefinitionObject);
    public int DefinitionObjectCount => Objects.Count(x => x.IsDefinitionObject);
}
