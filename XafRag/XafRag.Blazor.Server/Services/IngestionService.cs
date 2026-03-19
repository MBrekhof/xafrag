using Microsoft.EntityFrameworkCore;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Services;

public class IngestionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        IServiceScopeFactory scopeFactory,
        ILogger<IngestionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void IngestArticleInBackground(int articleId, string title, string content)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
                var chunkingService = scope.ServiceProvider.GetRequiredService<ChunkingService>();
                var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

                _logger.LogInformation("Ingesting article {Id}: {Title}", articleId, title);

                await ragDb.KnowledgeChunks
                    .Where(c => c.KnowledgeArticleId == articleId)
                    .ExecuteDeleteAsync();

                var chunks = chunkingService.ChunkText(content);
                if (chunks.Count == 0) return;

                var texts = chunks.Select(c => c.Content).ToList();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);

                for (int i = 0; i < chunks.Count; i++)
                {
                    ragDb.KnowledgeChunks.Add(new KnowledgeChunk
                    {
                        Content = chunks[i].Content,
                        Embedding = embeddings[i],
                        TokenCount = chunks[i].TokenCount,
                        ChunkIndex = chunks[i].ChunkIndex,
                        SourceType = ChunkSourceType.Article,
                        KnowledgeArticleId = articleId
                    });
                }

                await ragDb.SaveChangesAsync();
                _logger.LogInformation("Ingested {Count} chunks for article {Id}", chunks.Count, articleId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest article {Id}", articleId);
            }
        });
    }

    public void IngestDocumentInBackground(int documentId, string fileName, byte[] fileBytes)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
                var chunkingService = scope.ServiceProvider.GetRequiredService<ChunkingService>();
                var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
                var docProcessor = scope.ServiceProvider.GetRequiredService<DocumentProcessingService>();

                _logger.LogInformation("Ingesting document {Id}: {FileName}", documentId, fileName);

                var text = docProcessor.ExtractText(fileBytes, fileName);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("No text extracted from document {Id}", documentId);
                    return;
                }

                await ragDb.KnowledgeChunks
                    .Where(c => c.DocumentId == documentId)
                    .ExecuteDeleteAsync();

                var chunks = chunkingService.ChunkText(text);
                if (chunks.Count == 0) return;

                var texts = chunks.Select(c => c.Content).ToList();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);

                for (int i = 0; i < chunks.Count; i++)
                {
                    ragDb.KnowledgeChunks.Add(new KnowledgeChunk
                    {
                        Content = chunks[i].Content,
                        Embedding = embeddings[i],
                        TokenCount = chunks[i].TokenCount,
                        ChunkIndex = chunks[i].ChunkIndex,
                        SourceType = ChunkSourceType.Document,
                        DocumentId = documentId
                    });
                }

                await ragDb.SaveChangesAsync();
                _logger.LogInformation("Ingested {Count} chunks for document {Id}", chunks.Count, documentId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest document {Id}", documentId);
            }
        });
    }
}
