using System.Drawing;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

namespace ModelAuditor.Infrastructure;

internal sealed class AuditLocationPreview : DisplayConduit
{
    private readonly uint _documentSerialNumber;
    private readonly Guid _viewportId;
    private readonly IReadOnlyList<BoundingBox> _boxes;
    private readonly BoundingBox _totalBounds;
    private static readonly Color Outline = Color.FromArgb(255, 167, 37);

    public AuditLocationPreview(RhinoDoc document, RhinoViewport viewport,
        IReadOnlyList<BoundingBox> boxes, BoundingBox totalBounds)
    {
        _documentSerialNumber = document.RuntimeSerialNumber;
        _viewportId = viewport.Id;
        _boxes = boxes;
        _totalBounds = totalBounds;
    }

    private bool IsTargetView(DrawEventArgs e) =>
        e.RhinoDoc?.RuntimeSerialNumber == _documentSerialNumber && e.Viewport.Id == _viewportId;

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        if (IsTargetView(e))
            e.IncludeBoundingBox(_totalBounds);
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
        if (!IsTargetView(e))
            return;

        foreach (var bounds in _boxes)
            e.Display.DrawBox(bounds, Outline, 3);
    }
}
