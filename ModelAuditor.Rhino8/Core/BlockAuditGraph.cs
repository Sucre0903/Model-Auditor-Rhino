namespace ModelAuditor.Core;

internal sealed class BlockAuditGraph
{
    private readonly Dictionary<Guid, BlockAuditResult> _blocks = new();
    private readonly Dictionary<Guid, Dictionary<Guid, int>> _children = new();
    private readonly Dictionary<Guid, List<(Guid ParentId, int Count)>> _parents = new();
    private readonly Dictionary<Guid, List<Guid>> _topLevelObjects = new();

    public IReadOnlyCollection<BlockAuditResult> Blocks => _blocks.Values;
    public long MissingDefinitionReferences { get; private set; }
    public long CyclicReferences { get; private set; }
    public bool InstanceCountsCapped { get; private set; }

    public void AddDefinition(BlockAuditResult block)
    {
        _blocks.Add(block.DefinitionId, block);
        _children.Add(block.DefinitionId, new());
        _parents.Add(block.DefinitionId, new());
    }

    public BlockAuditResult? FindDefinition(Guid definitionId) => _blocks.GetValueOrDefault(definitionId);

    public void AddReference(Guid objectId, Guid? parentId, Guid definitionId)
    {
        if (!_blocks.ContainsKey(definitionId))
        {
            MissingDefinitionReferences++;
            return;
        }

        if (parentId is Guid parent)
        {
            var children = _children[parent];
            children[definitionId] = children.GetValueOrDefault(definitionId) + 1;
            return;
        }

        if (!_topLevelObjects.TryGetValue(definitionId, out var objects))
        {
            objects = new List<Guid>();
            _topLevelObjects.Add(definitionId, objects);
        }
        objects.Add(objectId);
    }

    public void Calculate(CancellationToken cancellation = default)
    {
        CyclicReferences = 0;
        InstanceCountsCapped = false;
        foreach (var block in _blocks.Values)
            block.InstanceDisplayComplexity = block.DefinitionDisplayComplexity;
        foreach (var parents in _parents.Values)
            parents.Clear();

        var definitionOrder = BuildDefinitionOrder(cancellation);

        // Children contribute their per-instance geometry to each parent once per reference.
        foreach (var definitionId in definitionOrder)
        {
            cancellation.ThrowIfCancellationRequested();
            var block = _blocks[definitionId];
            block.DirectInstanceCount = _topLevelObjects.GetValueOrDefault(definitionId)?.Count ?? 0;
            foreach (var (parentId, count) in _parents[definitionId])
            {
                var parent = _blocks[parentId];
                parent.InstanceDisplayComplexity = AddLoad(parent.InstanceDisplayComplexity, block.InstanceDisplayComplexity * count);
            }
        }

        // Walk from parents to children to count placements rooted in the document.
        for (var index = definitionOrder.Count - 1; index >= 0; index--)
        {
            var definitionId = definitionOrder[index];
            cancellation.ThrowIfCancellationRequested();
            var block = _blocks[definitionId];
            block.DocumentInstanceCount = block.DirectInstanceCount;
            foreach (var (parentId, count) in _parents[definitionId])
            {
                var parentPlacements = _blocks[parentId].DocumentInstanceCount;
                var available = long.MaxValue - block.DocumentInstanceCount;
                if (parentPlacements > available / count)
                {
                    InstanceCountsCapped = true;
                    block.DocumentInstanceCount = long.MaxValue;
                    break;
                }

                block.DocumentInstanceCount += parentPlacements * count;
            }
        }
    }

    public IReadOnlyList<Guid> GetTopLevelObjectIds(Guid definitionId) =>
        GetTopLevelObjectIds(new[] { definitionId });

    public IReadOnlyList<Guid> GetTopLevelObjectIds(IEnumerable<Guid> definitionIds)
    {
        var objects = new List<Guid>();
        var seen = new HashSet<Guid>();
        var pending = new Stack<Guid>();
        foreach (var definitionId in definitionIds)
        {
            if (_blocks.ContainsKey(definitionId) && seen.Add(definitionId))
                pending.Push(definitionId);
        }

        while (pending.TryPop(out var currentId))
        {
            if (_topLevelObjects.TryGetValue(currentId, out var roots))
                objects.AddRange(roots);

            foreach (var (parentId, _) in _parents[currentId])
            {
                if (seen.Add(parentId))
                    pending.Push(parentId);
            }
        }

        return objects;
    }

    private List<Guid> BuildDefinitionOrder(CancellationToken cancellation)
    {
        var finished = new List<Guid>(_blocks.Count);
        var active = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        var pending = new Stack<(Guid Id, Dictionary<Guid, int>.Enumerator Children)>();

        foreach (var rootId in _blocks.Keys)
        {
            if (!visited.Add(rootId))
                continue;

            active.Add(rootId);
            pending.Push((rootId, _children[rootId].GetEnumerator()));
            while (pending.TryPop(out var frame))
            {
                cancellation.ThrowIfCancellationRequested();
                if (!frame.Children.MoveNext())
                {
                    active.Remove(frame.Id);
                    finished.Add(frame.Id);
                    continue;
                }

                var (childId, count) = frame.Children.Current;
                pending.Push(frame);
                if (active.Contains(childId))
                {
                    CyclicReferences += count;
                    continue;
                }

                _parents[childId].Add((frame.Id, count));
                if (visited.Add(childId))
                {
                    active.Add(childId);
                    pending.Push((childId, _children[childId].GetEnumerator()));
                }
            }
        }

        return finished;
    }

    private static double AddLoad(double first, double second) => Math.Min(double.MaxValue, first + second);
}
