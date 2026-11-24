using Rhino;
using Rhino.Commands;
using Rhino.UI;
using System.Runtime.InteropServices;

namespace ModelAuditor.Commands;

[Guid("95C4EF11-FE89-4CA6-92B0-88410FBB752A")]
public sealed class ModelAuditCommand : Command
{
    public override string EnglishName => "ModelAudit";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        Panels.OpenPanel(UI.ModelAuditPanel.PanelId);
        return Result.Success;
    }
}
