using DevExpress.ExpressApp;
using DevExpress.ExpressApp.SystemModule;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class RagChatWindowController : WindowController
{
    public RagChatWindowController()
    {
        TargetWindowType = WindowType.Main;
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        var navController = Frame.GetController<ShowNavigationItemController>();
        if (navController != null)
        {
            navController.CustomShowNavigationItem += OnCustomShowNavigationItem;
        }
    }

    protected override void OnDeactivated()
    {
        var navController = Frame.GetController<ShowNavigationItemController>();
        if (navController != null)
        {
            navController.CustomShowNavigationItem -= OnCustomShowNavigationItem;
        }
        base.OnDeactivated();
    }

    private void OnCustomShowNavigationItem(object? sender, CustomShowNavigationItemEventArgs e)
    {
        if (e.ActionArguments.SelectedChoiceActionItem?.Data is ViewShortcut shortcut
            && shortcut.ViewId == "RagChatHolder_ListView")
        {
            var objectSpace = Application.CreateObjectSpace(typeof(RagChatHolder));
            var holder = objectSpace.CreateObject<RagChatHolder>();
            var detailView = Application.CreateDetailView(objectSpace, holder);
            detailView.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.View;
            e.ActionArguments.ShowViewParameters.CreatedView = detailView;
            e.Handled = true;
        }
    }
}
