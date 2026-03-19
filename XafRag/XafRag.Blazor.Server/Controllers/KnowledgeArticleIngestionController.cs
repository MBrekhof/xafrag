using DevExpress.ExpressApp;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class KnowledgeArticleIngestionController : ObjectViewController<DetailView, KnowledgeArticle>
{
    private Services.IngestionService? _ingestionService;

    protected override void OnActivated()
    {
        base.OnActivated();
        _ingestionService = Application.ServiceProvider.GetRequiredService<Services.IngestionService>();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        var article = ViewCurrentObject;
        if (article != null && !string.IsNullOrWhiteSpace(article.Content))
        {
            _ingestionService?.IngestArticleInBackground(article.Id, article.Title, article.Content);
        }
    }
}
