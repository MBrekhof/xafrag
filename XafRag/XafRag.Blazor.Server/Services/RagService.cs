using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using XafRag.Blazor.Server.Configuration;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Services;

public class SearchResult
{
    public string Content { get; set; } = string.Empty;
    public double Distance { get; set; }
    public ChunkSourceType SourceType { get; set; }
    public int? KnowledgeArticleId { get; set; }
    public int? DocumentId { get; set; }
}

public class RagService
{
    private readonly RagDbContext _ragDb;
    private readonly EmbeddingService _embeddingService;
    private readonly IChatClient _chatClient;
    private readonly RagOptions _options;
    private readonly ILogger<RagService> _logger;

    private const string SystemPrompt = """
        You are a helpful knowledge base assistant. Use the provided context to answer the user's question.
        If the context does not contain enough information to answer, say so honestly.
        Always base your answers on the provided context. Do not make up information.
        When possible, mention which source the information came from.
        Format your responses using Markdown for readability.
        """;

    public RagService(
        RagDbContext ragDb,
        EmbeddingService embeddingService,
        IChatClient chatClient,
        IOptions<RagOptions> options,
        ILogger<RagService> logger)
    {
        _ragDb = ragDb;
        _embeddingService = embeddingService;
        _chatClient = chatClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var queryVector = await _embeddingService.GenerateEmbeddingAsync(query, ct);

        var results = await _ragDb.KnowledgeChunks
            .Select(k => new SearchResult
            {
                Content = k.Content,
                Distance = k.Embedding!.CosineDistance(queryVector),
                SourceType = k.SourceType,
                KnowledgeArticleId = k.KnowledgeArticleId,
                DocumentId = k.DocumentId
            })
            .Where(r => r.Distance <= _options.DistanceThreshold)
            .OrderBy(r => r.Distance)
            .Take(_options.MaxResults)
            .ToListAsync(ct);

        _logger.LogInformation("RAG search for '{Query}' returned {Count} results", query, results.Count);
        return results;
    }

    public async IAsyncEnumerable<string> AskAsync(
        string question,
        IList<ChatMessage>? conversationHistory = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var searchResults = await SearchAsync(question, ct);

        var contextText = searchResults.Count > 0
            ? string.Join("\n\n---\n\n", searchResults.Select((r, i) =>
                $"[Source {i + 1} - {r.SourceType}] {r.Content}"))
            : "No relevant context found in the knowledge base.";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $"{SystemPrompt}\n\n## Context:\n{contextText}")
        };

        if (conversationHistory != null)
        {
            messages.AddRange(conversationHistory);
        }

        messages.Add(new(ChatRole.User, question));

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is { } text)
            {
                yield return text;
            }
        }
    }
}
