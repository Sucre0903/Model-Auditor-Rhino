using System.Runtime.InteropServices;
using ModelAuditor.UI;
using Rhino;
using Rhino.PlugIns;
using Rhino.UI;

namespace ModelAuditor;

[Guid("8B8F3B7E-5359-4B30-9E3B-0F0D6E5AD3A1")]
public sealed class ModelAuditorPlugin : PlugIn
{
    private const string PanelIconResource = "ModelAuditor.Resources.ModelAudit.ico";
    private const string InitialDockingSetting = "InitialPanelDockingComplete";
    private int _dockingAttempts;

    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        try
        {
            Panels.RegisterPanel(this, typeof(ModelAuditPanel), "模型体检",
                typeof(ModelAuditorPlugin).Assembly, PanelIconResource, PanelType.PerDoc);
            if (!Settings.GetBool(InitialDockingSetting, false))
                RhinoApp.Idle += DockPanelOnIdle;
            return LoadReturnCode.Success;
        }
        catch (Exception ex)
        {
            errorMessage = $"模型体检面板注册失败：{ex.Message}";
            return LoadReturnCode.ErrorShowDialog;
        }
    }

    private void DockPanelOnIdle(object? sender, EventArgs e)
    {
        if (RhinoDoc.ActiveDoc is null)
            return;

        var panelId = ModelAuditPanel.PanelId;
        if (!Panels.IsPanelVisible(panelId) &&
            !Panels.OpenPanelAsSibling(panelId, PanelIds.Layers, makeSelectedPanel: false))
            Panels.OpenPanel(panelId, makeSelectedPanel: false);

        if (Panels.IsPanelVisible(panelId))
        {
            RhinoApp.Idle -= DockPanelOnIdle;
            Settings.SetBool(InitialDockingSetting, true);
        }
        else if (++_dockingAttempts >= 3)
        {
            RhinoApp.Idle -= DockPanelOnIdle;
        }
    }
}
