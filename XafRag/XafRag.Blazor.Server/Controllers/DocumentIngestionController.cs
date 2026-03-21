using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class DocumentIngestionController : ObjectViewController<DetailView, Document>
{
    private Services.IngestionService? _ingestionService;
    private ILogger<DocumentIngestionController>? _logger;
    private bool _isCommitting;

    protected override void OnActivated()
    {
        base.OnActivated();
        _ingestionService = Application.ServiceProvider.GetRequiredService<Services.IngestionService>();
        _logger = Application.ServiceProvider.GetRequiredService<ILogger<DocumentIngestionController>>();
        ObjectSpace.Committed += ObjectSpace_Committed;
    }

    protected override void OnDeactivated()
    {
        ObjectSpace.Committed -= ObjectSpace_Committed;
        base.OnDeactivated();
    }

    private void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        if (_isCommitting) return;

        var doc = ViewCurrentObject;
        _logger?.LogInformation("Committed fired for document {Id}, FileName={FileName}, Status={Status}",
            doc?.Id, doc?.FileName, doc?.Status);

        if (doc == null)
        {
            _logger?.LogWarning("ViewCurrentObject is null");
            return;
        }

        if (doc.FileData == null)
        {
            _logger?.LogWarning("Document {Id}: FileData is null", doc.Id);
            return;
        }

        if (doc.Status != DocumentStatus.Pending)
        {
            _logger?.LogInformation("Document {Id}: status is {Status}, skipping", doc.Id, doc.Status);
            return;
        }

        _logger?.LogInformation("Document {Id}: reading file bytes from FileData (Size={Size})", doc.Id, doc.FileData.Size);

        using var ms = new MemoryStream();
        doc.FileData.SaveToStream(ms);
        var bytes = ms.ToArray();

        _logger?.LogInformation("Document {Id}: got {ByteCount} bytes", doc.Id, bytes.Length);

        if (bytes.Length == 0)
        {
            _logger?.LogWarning("Document {Id}: file is empty", doc.Id);
            return;
        }

        var fileName = !string.IsNullOrEmpty(doc.FileName) ? doc.FileName : doc.FileData.FileName;
        _logger?.LogInformation("Document {Id}: resolved fileName={FileName}", doc.Id, fileName);

        _isCommitting = true;
        try
        {
            doc.Status = DocumentStatus.Processing;
            ObjectSpace.CommitChanges();
        }
        finally
        {
            _isCommitting = false;
        }

        _logger?.LogInformation("Document {Id}: dispatching to IngestionService", doc.Id);
        _ingestionService?.IngestDocumentInBackground(doc.Id, fileName, bytes);
    }
}
