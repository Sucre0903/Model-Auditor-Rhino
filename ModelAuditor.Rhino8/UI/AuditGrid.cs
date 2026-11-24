using Eto.Forms;
using ModelAuditor.Core;

namespace ModelAuditor.UI;

internal sealed class AuditGrid<T> : GridView<T> where T : class
{
    private readonly Dictionary<GridColumn, (string Title, Func<T, IComparable> Key)> _sortColumns = new();
    private IReadOnlyList<T> _rows = Array.Empty<T>();
    private GridColumn? _sortColumn;
    private bool _descending;
    private long _sortRequest;

    public AuditGrid()
    {
        ShowHeader = true;
        AllowMultipleSelection = true;
        ColumnHeaderClick += (_, e) => RequestSort(e.Column);
    }

    public bool IsReordering { get; private set; }

    public GridColumn AddColumn<TValue>(string title, int width, Func<T, TValue> value,
        Func<TValue, string>? format = null) where TValue : IComparable
    {
        var column = new GridColumn
        {
            HeaderText = title,
            Width = width,
            Sortable = true,
            DataCell = new TextBoxCell
            {
                Binding = Binding.Delegate<T, string>(row =>
                    format is null ? value(row).ToString() ?? "" : format(value(row)))
            }
        };
        _sortColumns.Add(column, (title, row => value(row)));
        Columns.Add(column);
        return column;
    }

    public void SetRows(IReadOnlyList<T> rows)
    {
        CancelPendingSort();
        ResetSortHeader();
        _sortColumn = null;
        _rows = rows;
        DataStore = rows;
    }

    public void CancelPendingSort() => _sortRequest++;

    private void ResetSortHeader()
    {
        if (_sortColumn is not null)
            _sortColumn.HeaderText = _sortColumns[_sortColumn].Title;
    }

    private async void RequestSort(GridColumn column)
    {
        if (_rows.Count == 0 || !_sortColumns.TryGetValue(column, out var spec))
            return;

        ResetSortHeader();
        _descending = ReferenceEquals(_sortColumn, column) && !_descending;
        _sortColumn = column;
        column.HeaderText = spec.Title + (_descending ? " ▼" : " ▲");
        var descending = _descending;
        var rows = _rows;
        var request = ++_sortRequest;
        var app = Application.Instance;
        var sorted = await Task.Run(() => AuditOrdering.Sort(rows, spec.Key, descending)).ConfigureAwait(false);
        app.AsyncInvoke(() =>
        {
            if (request != _sortRequest)
                return;

            var selectedRows = AuditOrdering.FindSelectedRows(sorted, SelectedItems);
            IsReordering = true;
            try
            {
                _rows = sorted;
                DataStore = sorted;
                SelectedRows = selectedRows;
                if (selectedRows.Count > 0)
                    ScrollToRow(selectedRows[0]);
            }
            finally
            {
                IsReordering = false;
            }
        });
    }
}
