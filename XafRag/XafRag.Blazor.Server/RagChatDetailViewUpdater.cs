using DevExpress.ExpressApp.Model;
using DevExpress.ExpressApp.Model.Core;
using DevExpress.ExpressApp.Model.NodeGenerators;
using XafRag.Blazor.Server.Editors;

namespace XafRag.Blazor.Server;

public class RagChatDetailViewUpdater : ModelNodesGeneratorUpdater<ModelViewsNodesGenerator>
{
    public override void UpdateNode(ModelNode node)
    {
        var views = (IModelViews)node;

        // Debug: log all view IDs containing "RagChat"
        foreach (var viewNode in views)
        {
            if (viewNode.Id.Contains("RagChat", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[RagChatUpdater] Found view: {viewNode.Id} (type: {viewNode.GetType().Name})");
            }
        }

        if (views["RagChatHolder_DetailView"] is not IModelDetailView dv)
        {
            Console.WriteLine("[RagChatUpdater] RagChatHolder_DetailView NOT found!");
            return;
        }

        Console.WriteLine("[RagChatUpdater] RagChatHolder_DetailView found, configuring layout...");

        const string chatItemId = "RagChatItem";
        if (dv.Items[chatItemId] == null)
        {
            dv.Items.AddNode<IModelRagChatViewItem>(chatItemId);
        }

        var oidItem = dv.Items["Oid"];
        if (oidItem != null)
            ((IModelNode)oidItem).Remove();

        var layout = dv.Layout;
        if (layout == null)
            return;

        for (int i = layout.Count - 1; i >= 0; i--)
        {
            layout[i].Remove();
        }

        var chatLayoutItem = layout.AddNode<IModelLayoutViewItem>(chatItemId);
        chatLayoutItem.ViewItem = (IModelViewItem)dv.Items[chatItemId];
    }
}
