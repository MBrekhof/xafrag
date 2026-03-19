using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;

namespace XafRag.Blazor.Server.Editors;

public interface IModelRagChatViewItem : IModelViewItem { }

[ViewItem(typeof(IModelRagChatViewItem))]
public class RagChatViewItem(IModelViewItem model, Type objectType)
    : ViewItem(objectType, model.Id),
      IComponentContentHolder,
      IComplexViewItem
{
    private RagChatComponentModel? _componentModel;
    private XafApplication? _application;

    public RagChatComponentModel ComponentModel => _componentModel!;

    RenderFragment IComponentContentHolder.ComponentContent =>
        ComponentModelObserver.Create(_componentModel!, _componentModel!.GetComponentContent());

    void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
    {
        _application = application;
    }

    protected override object CreateControlCore()
    {
        _componentModel = new RagChatComponentModel();
        return _componentModel;
    }
}

public class RagChatComponentModel : ComponentModelBase
{
    public override Type ComponentType => typeof(RagChatComponent);
}
