using DevExpress.Pdf;
using DevExpress.XtraRichEdit;

namespace XafRag.Blazor.Server.Services;

public class DocumentProcessingService
{
    private readonly ILogger<DocumentProcessingService> _logger;

    public DocumentProcessingService(ILogger<DocumentProcessingService> logger)
    {
        _logger = logger;
    }

    public string ExtractText(byte[] fileBytes, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".txt" or ".md" => System.Text.Encoding.UTF8.GetString(fileBytes),
            ".pdf" => ExtractFromPdf(fileBytes),
            ".docx" => ExtractFromDocx(fileBytes),
            _ => throw new NotSupportedException($"Unsupported file type: {extension}")
        };
    }

    private string ExtractFromPdf(byte[] fileBytes)
    {
        try
        {
            using var processor = new PdfDocumentProcessor();
            using var stream = new MemoryStream(fileBytes);
            // detachStreamAfterLoadComplete=true allows the stream to be disposed independently
            processor.LoadDocument(stream, detachStreamAfterLoadComplete: true);
            return processor.GetText();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from PDF");
            throw;
        }
    }

    private string ExtractFromDocx(byte[] fileBytes)
    {
        try
        {
            using var stream = new MemoryStream(fileBytes);
            using var processor = new RichEditDocumentServer();
            processor.LoadDocument(stream, DocumentFormat.OpenXml);
            return processor.Document.GetText(processor.Document.Range);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from DOCX");
            throw;
        }
    }
}
