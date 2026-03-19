using DevExpress.ExpressApp;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class DocumentIngestionController : ObjectViewController<DetailView, Document>
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
        var doc = ViewCurrentObject;
        if (doc?.FileData?.Content != null && doc.FileData.Size > 0)
        {
            using var ms = new MemoryStream();
            doc.FileData.SaveToStream(ms);
            var bytes = ms.ToArray();

            doc.Status = DocumentStatus.Processing;
            _ingestionService?.IngestDocumentInBackground(doc.Id, doc.FileName, bytes);
        }
    }
}
