using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;

namespace ModelAuditor.Infrastructure;

internal sealed class AuditDocumentWatch : IDisposable
{
    private readonly Action<AuditDocumentWatch, bool> _invalidate;
    private int _changed;
    private int _disposed;

    public AuditDocumentWatch(RhinoDoc document, Action<AuditDocumentWatch, bool> invalidate)
    {
        SerialNumber = document.RuntimeSerialNumber;
        _invalidate = invalidate;
        RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
        RhinoDoc.CloseDocument += OnCloseDocument;
        RhinoDoc.AddRhinoObject += OnObject;
        RhinoDoc.DeleteRhinoObject += OnObject;
        RhinoDoc.UndeleteRhinoObject += OnObject;
        RhinoDoc.ReplaceRhinoObject += OnReplace;
        RhinoDoc.ModifyObjectAttributes += OnAttributes;
        RhinoDoc.LayerTableEvent += OnLayer;
        RhinoDoc.InstanceDefinitionTableEvent += OnBlock;
    }

    public uint SerialNumber { get; }
    public bool HasChanged => Volatile.Read(ref _changed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
        RhinoDoc.CloseDocument -= OnCloseDocument;
        RhinoDoc.AddRhinoObject -= OnObject;
        RhinoDoc.DeleteRhinoObject -= OnObject;
        RhinoDoc.UndeleteRhinoObject -= OnObject;
        RhinoDoc.ReplaceRhinoObject -= OnReplace;
        RhinoDoc.ModifyObjectAttributes -= OnAttributes;
        RhinoDoc.LayerTableEvent -= OnLayer;
        RhinoDoc.InstanceDefinitionTableEvent -= OnBlock;
    }

    private void OnActiveDocumentChanged(object? sender, DocumentEventArgs e)
    {
        if (e.DocumentSerialNumber != SerialNumber)
            MarkChanged(documentUnavailable: true);
    }

    private void OnCloseDocument(object? sender, DocumentEventArgs e)
    {
        if (e.DocumentSerialNumber == SerialNumber)
            MarkChanged(documentUnavailable: true);
    }

    private void OnObject(object? sender, RhinoObjectEventArgs e)
    {
        if (!HasChanged)
            MarkIfWatched(e.TheObject?.Document ?? RhinoDoc.ActiveDoc);
    }

    private void OnReplace(object? sender, RhinoReplaceObjectEventArgs e) => MarkIfWatched(e.Document);
    private void OnBlock(object? sender, InstanceDefinitionTableEventArgs e) => MarkIfWatched(e.Document);

    private void OnAttributes(object? sender, RhinoModifyObjectAttributesEventArgs e)
    {
        if (HasChanged || e.Document?.RuntimeSerialNumber != SerialNumber)
            return;

        var old = e.OldAttributes;
        var current = e.NewAttributes;
        // Visibility and locking can change after a scan. Locate reads those states live.
        if (old is not null && current is not null && old.Mode != current.Mode &&
            old.LayerIndex == current.LayerIndex && old.Name == current.Name &&
            old.UserData.Count == current.UserData.Count && SameUserStrings(old, current))
            return;

        MarkChanged(documentUnavailable: false);
    }

    private static bool SameUserStrings(ObjectAttributes old, ObjectAttributes current)
    {
        var before = old.GetUserStrings();
        var after = current.GetUserStrings();
        if (before?.Count != after?.Count)
            return false;

        return before is null || before.AllKeys.All(key => before[key] == after?[key]);
    }

    private void OnLayer(object? sender, LayerTableEventArgs e)
    {
        if (HasChanged || e.Document?.RuntimeSerialNumber != SerialNumber ||
            e.EventType is LayerTableEventType.Current or LayerTableEventType.Sorted)
            return;

        if (e.EventType == LayerTableEventType.Modified && e.OldState is { } old &&
            e.NewState is { } current && old.Name == current.Name && old.ParentLayerId == current.ParentLayerId)
            return;

        MarkChanged(documentUnavailable: false);
    }

    private void MarkIfWatched(RhinoDoc? document)
    {
        if (document?.RuntimeSerialNumber == SerialNumber)
            MarkChanged(documentUnavailable: false);
    }

    private void MarkChanged(bool documentUnavailable)
    {
        if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _changed, 1) == 0)
            _invalidate(this, documentUnavailable);
    }
}
