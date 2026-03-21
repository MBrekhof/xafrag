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

                _logger.LogInformation("Ingesting document {Id}: {FileName} ({ByteCount} bytes)", documentId, fileName, fileBytes.Length);

                _logger.LogInformation("Document {Id}: extracting text...", documentId);
                var text = docProcessor.ExtractText(fileBytes, fileName);
                _logger.LogInformation("Document {Id}: extracted {CharCount} characters", documentId, text?.Length ?? 0);

                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("No text extracted from document {Id}", documentId);
                    await UpdateDocumentStatus(ragDb, documentId, DocumentStatus.Failed);
                    return;
                }

                _logger.LogInformation("Document {Id}: deleting old chunks...", documentId);
                var deleted = await ragDb.KnowledgeChunks
                    .Where(c => c.DocumentId == documentId)
                    .ExecuteDeleteAsync();
                _logger.LogInformation("Document {Id}: deleted {Count} old chunks", documentId, deleted);

                _logger.LogInformation("Document {Id}: chunking text...", documentId);
                var chunks = chunkingService.ChunkText(text);
                _logger.LogInformation("Document {Id}: produced {Count} chunks", documentId, chunks.Count);
                if (chunks.Count == 0)
                {
                    _logger.LogWarning("Document {Id}: no chunks produced, marking failed", documentId);
                    await UpdateDocumentStatus(ragDb, documentId, DocumentStatus.Failed);
                    return;
                }

                var texts = chunks.Select(c => c.Content).ToList();
                _logger.LogInformation("Document {Id}: generating embeddings for {Count} chunks...", documentId, texts.Count);
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);
                _logger.LogInformation("Document {Id}: embeddings generated", documentId);

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

                _logger.LogInformation("Document {Id}: saving chunks to database...", documentId);
                await ragDb.SaveChangesAsync();
                await UpdateDocumentStatus(ragDb, documentId, DocumentStatus.Completed);
                _logger.LogInformation("Document {Id}: ingestion complete — {Count} chunks saved", documentId, chunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest document {Id}", documentId);
                try
                {
                    using var errorScope = _scopeFactory.CreateScope();
                    var errorDb = errorScope.ServiceProvider.GetRequiredService<RagDbContext>();
                    await UpdateDocumentStatus(errorDb, documentId, DocumentStatus.Failed);
                }
                catch (Exception statusEx)
                {
                    _logger.LogError(statusEx, "Failed to update status for document {Id}", documentId);
                }
            }
        });
    }

    private static async Task UpdateDocumentStatus(RagDbContext ragDb, int documentId, DocumentStatus status)
    {
        await ragDb.Database.ExecuteSqlRawAsync(
            """UPDATE "Documents" SET "Status" = {0} WHERE "Id" = {1}""",
            (int)status, documentId);
    }
}
