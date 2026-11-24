namespace ModelAuditor.Core;

internal static class AuditOrdering
{
    public static T[] Sort<T>(IReadOnlyList<T> rows, Func<T, IComparable> key, bool descending) =>
        (descending ? rows.OrderByDescending(key) : rows.OrderBy(key)).ToArray();

    public static IReadOnlyList<int> FindSelectedRows<T>(IReadOnlyList<T> rows, IEnumerable<T> selection)
        where T : class
    {
        var selectedItems = new HashSet<T>(selection, ReferenceEqualityComparer.Instance);
        var selectedIndices = new List<int>(selectedItems.Count);
        for (var index = 0; index < rows.Count && selectedIndices.Count < selectedItems.Count; index++)
        {
            if (selectedItems.Contains(rows[index]))
                selectedIndices.Add(index);
        }
        return selectedIndices;
    }
}
