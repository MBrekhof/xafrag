# How to Add RAG to Your Own XAF Application

This guide explains how to replicate the patterns from the XafRag sample in your own DevExpress XAF Blazor Server application. It assumes you already have a working XAF Blazor Server project targeting .NET 8.

For the complete implementations of every file mentioned here, refer to the source code in this repository.

---

## 1. Prerequisites

Before you start:

- .NET 8 SDK and a DevExpress XAF 25.2.x Blazor Server project
- Docker (for the PGVector database)
- An OpenAI API key (for `text-embedding-3-small` and `gpt-4o`)
- DevExpress NuGet feed configured in your `nuget.config` or Visual Studio package sources

---

## 2. Add Docker for PGVector

Create (or extend) `docker-compose.yml` at your repo root:

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg18
    environment:
      POSTGRES_DB: YourDb
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: password
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data

volumes:
  postgres-data:
```

Use `pgvector/pgvector:pg18` (or `pg16`/`pg17`) rather than the plain `postgres` image — it has the `vector` extension pre-installed.

---

## 3. Install NuGet Packages

### XafRag.Module

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.2.2" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.8" />
```

### XafRag.Blazor.Server

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.2.2" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.8" />
<PackageReference Include="Microsoft.Extensions.AI" Version="9.7.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.7.1-preview.1.25365.4" />
<PackageReference Include="DevExpress.AIIntegration.Blazor.Chat" Version="25.2.3" />
<PackageReference Include="DevExpress.Document.Processor" Version="25.2.3" />
<PackageReference Include="Markdig" Version="0.42.0" />
<PackageReference Include="HtmlSanitizer" Version="8.1.870" />
```

**Compatibility note:** `Pgvector.EntityFrameworkCore` 0.2.2 is required for EF Core 8. Version 0.3.x targets EF Core 9+. Do not upgrade it unless you also upgrade EF Core.

---

## 4. Create the Data Model

### XAF business objects (in the Module project)

`KnowledgeArticle` is a standard XAF EF Core entity with a `[NavigationItem]` attribute so it appears in the sidebar:

```csharp
[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DefaultProperty(nameof(Title))]
public class KnowledgeArticle : IXafEntityObject
{
    [Key]
    public virtual int Id { get; set; }

    [FieldSize(200)]
    public virtual string Title { get; set; } = string.Empty;

    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string Content { get; set; } = string.Empty;

    public virtual DateTime CreatedDate { get; set; }
    public virtual DateTime ModifiedDate { get; set; }

    public void OnCreated() { CreatedDate = ModifiedDate = DateTime.UtcNow; }
    public void OnSaving()  { ModifiedDate = DateTime.UtcNow; }
    public void OnLoaded()  { }
}
```

`Document` uses XAF's built-in `FileData` type to handle binary uploads.

For the chat view, create a **non-persistent** placeholder object. XAF needs a business object to open a Detail View, even when there is no database-backed record:

```csharp
[DomainComponent]
[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DisplayName("RAG Chat")]
public class RagChatHolder : NonPersistentBaseObject { }
```

Register `KnowledgeArticle` and `Document` as `DbSet` properties in your `XafEFCoreDbContext`. Do not register `RagChatHolder` — it is non-persistent.

### KnowledgeChunk and RagDbContext (in the Module project)

`KnowledgeChunk` stores the text chunks and their embeddings. It uses conventional EF Core data annotations rather than XAF attributes because XAF must not manage this entity:

```csharp
[Table("knowledge_chunks")]
public class KnowledgeChunk
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("embedding", TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }

    [Column("token_count")]
    public int TokenCount { get; set; }

    [Column("chunk_index")]
    public int ChunkIndex { get; set; }

    [Column("source_type")]
    public ChunkSourceType SourceType { get; set; }  // Article or Document enum

    [Column("knowledge_article_id")]
    public int? KnowledgeArticleId { get; set; }

    [Column("document_id")]
    public int? DocumentId { get; set; }
}
```

The dimension `vector(1536)` matches `text-embedding-3-small`. Use `vector(3072)` for `text-embedding-3-large`.

`RagDbContext` is a standalone `DbContext` that is completely separate from XAF's `XafEFCoreDbContext`:

```csharp
public class RagDbContext : DbContext
{
    public RagDbContext(DbContextOptions<RagDbContext> options) : base(options) { }

    public DbSet<KnowledgeChunk> KnowledgeChunks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<KnowledgeChunk>(e =>
        {
            e.HasIndex(x => x.KnowledgeArticleId);
            e.HasIndex(x => x.DocumentId);
        });
    }
}
```

Why keep it separate? XAF's EF Core context uses `TypesInfoInitializer`, change-tracking strategies, optimistic locking, and deferred deletion that are incompatible with the raw vector operations PGVector requires. A dedicated `RagDbContext` avoids all of that friction.

---

## 5. Build the Services

All services go in the Blazor Server project under `Services/`.

### ChunkingService

Splits text into overlapping chunks at paragraph boundaries. Paragraphs that exceed the token limit are split further at sentence boundaries:

```csharp
// Core loop — see ChunkingService.cs for the full implementation
var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);
// accumulate paragraphs until ChunkTokenLimit, then flush with overlap
```

Token count is estimated as `text.Length / 4` (a rough approximation; replace with a proper tokenizer if accuracy matters). Default settings: 500 tokens per chunk, 100-token overlap.

### EmbeddingService

Wraps `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`:

```csharp
public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
{
    var result = await _embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
    return new Vector(result[0].Vector);
}
```

The `Vector` type is from the `Pgvector` package and maps to the `vector` PostgreSQL column type.

### DocumentProcessingService

Uses DevExpress `PdfDocumentProcessor` and `RichEditDocumentServer` (from `DevExpress.Document.Processor`) to extract plain text from PDF and DOCX files:

```csharp
return extension switch
{
    ".txt" or ".md" => Encoding.UTF8.GetString(fileBytes),
    ".pdf"  => ExtractFromPdf(fileBytes),    // PdfDocumentProcessor.GetText()
    ".docx" => ExtractFromDocx(fileBytes),   // RichEditDocumentServer + OpenXml
    _ => throw new NotSupportedException($"Unsupported file type: {extension}")
};
```

### RagService

Combines vector search with LLM chat completion. The `AskAsync` method is an async stream so responses can be rendered token-by-token:

```csharp
public async IAsyncEnumerable<string> AskAsync(string question, ...)
{
    // 1. Embed the question
    var queryVector = await _embeddingService.GenerateEmbeddingAsync(question, ct);

    // 2. Vector search via PGVector cosine distance
    var results = await _ragDb.KnowledgeChunks
        .Select(k => new { k.Content, Distance = k.Embedding!.CosineDistance(queryVector), ... })
        .Where(r => r.Distance <= _options.DistanceThreshold)
        .OrderBy(r => r.Distance)
        .Take(_options.MaxResults)
        .ToListAsync(ct);

    // 3. Build augmented prompt
    var context = string.Join("\n\n---\n\n", results.Select((r, i) =>
        $"[Source {i + 1}] {r.Content}"));
    var messages = new List<ChatMessage>
    {
        new(ChatRole.System, $"{SystemPrompt}\n\n## Context:\n{context}"),
        new(ChatRole.User, question)
    };

    // 4. Stream LLM response
    await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, ct: ct))
        if (update.Text is { } text) yield return text;
}
```

---

## 6. Wire Up Ingestion

### IngestionService

Register as a **singleton** because it only holds a `IServiceScopeFactory` reference. Each background task creates its own DI scope to resolve scoped services (`RagDbContext`, `ChunkingService`, `EmbeddingService`):

```csharp
public void IngestArticleInBackground(int articleId, string title, string content)
{
    _ = Task.Run(async () =>
    {
        using var scope = _scopeFactory.CreateScope();
        var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
        // ... chunk, embed, delete old chunks, insert new ones, SaveChangesAsync
    });
}
```

Exceptions are caught and logged so a failed ingestion never crashes the web request.

### XAF ViewControllers

Create an `ObjectViewController<DetailView, KnowledgeArticle>` in the Blazor Server project (not the Module project, because it depends on `IngestionService`):

```csharp
public class KnowledgeArticleIngestionController
    : ObjectViewController<DetailView, KnowledgeArticle>
{
    private IngestionService? _ingestionService;

    protected override void OnActivated()
    {
        base.OnActivated();
        _ingestionService = Application.ServiceProvider
            .GetRequiredService<IngestionService>();
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
            _ingestionService?.IngestArticleInBackground(
                article.Id, article.Title, article.Content);
    }
}
```

Create a matching controller for `Document`. In that controller, read the file bytes from `doc.FileData` before passing them to `IngestDocumentInBackground` — the `FileData` object may not be accessible from the background thread.

---

## 7. Add the Chat UI

### Custom ViewItem

XAF Blazor renders custom UI through `IComponentContentHolder`. Create a `ViewItem` subclass that holds a `ComponentModelBase` and returns a `RenderFragment`:

```csharp
[ViewItem(typeof(IModelRagChatViewItem))]
public class RagChatViewItem(IModelViewItem model, Type objectType)
    : ViewItem(objectType, model.Id),
      IComponentContentHolder,
      IComplexViewItem
{
    private RagChatComponentModel? _componentModel;

    RenderFragment IComponentContentHolder.ComponentContent =>
        ComponentModelObserver.Create(_componentModel!, _componentModel!.GetComponentContent());

    protected override object CreateControlCore()
    {
        _componentModel = new RagChatComponentModel();
        return _componentModel;
    }

    void IComplexViewItem.Setup(IObjectSpace os, XafApplication app) { }
}

public class RagChatComponentModel : ComponentModelBase
{
    public override Type ComponentType => typeof(RagChatComponent);
}
```

The `IModelRagChatViewItem` interface (extending `IModelViewItem`) is registered automatically by XAF through the `[ViewItem]` attribute.

After creating `RagChatViewItem`, open the XAF Application Model editor and add an instance of `RagChatViewItem` to the `RagChatHolder_DetailView` view's Items collection.

### Razor component

```razor
@inject RagService RagService

<DxAIChat UseStreaming="false"
          ResponseContentFormat="ResponseContentFormat.Markdown"
          MessageSent="OnMessageSent">
    ...
</DxAIChat>

@code {
    private async Task OnMessageSent(MessageSentEventArgs args)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in RagService.AskAsync(args.Content, ct: args.CancellationToken))
            sb.Append(chunk);
        await args.Chat.SendMessage(sb.ToString(), ChatRole.Assistant);
    }
}
```

`DxAIChat` is injected with the registered `IChatClient`, but this component bypasses it and calls `RagService.AskAsync` directly so that retrieval-augmented context is injected before the LLM call.

For Markdown rendering, add `Markdig` and `HtmlSanitizer` packages and convert the response before displaying.

---

## 8. Register Everything in Startup.cs

Add the following to `ConfigureServices`, before `AddXaf`:

```csharp
// 1. NpgsqlDataSource with vector support (strip the XAF EFCoreProvider prefix first)
var pgConnStr = Configuration.GetConnectionString("ConnectionString")!
    .Replace("EFCoreProvider=Postgres;", "");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(pgConnStr);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

services.AddDbContext<RagDbContext>((sp, options) =>
    options.UseNpgsql(dataSource, o => o.UseVector()));

// 2. Configuration sections
services.Configure<OpenAiOptions>(Configuration.GetSection("OpenAI"));
services.Configure<RagOptions>(Configuration.GetSection("Rag"));

// 3. OpenAI clients via Microsoft.Extensions.AI abstractions
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY is not set.");
var openAiClient = new OpenAIClient(apiKey);

IChatClient chatClient = openAiClient
    .GetChatClient("gpt-4o").AsIChatClient();
services.AddChatClient(chatClient);

IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = openAiClient
    .GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();
services.AddSingleton(embeddingGenerator);

// 4. DevExpress AI
services.AddDevExpressBlazor();
services.AddDevExpressAI();

// 5. RAG services
services.AddScoped<ChunkingService>();
services.AddScoped<EmbeddingService>();
services.AddScoped<DocumentProcessingService>();
services.AddScoped<RagService>();
services.AddSingleton<IngestionService>();
```

Then in `Configure`, after `UseXaf()`, ensure the `RagDbContext` schema is created:

```csharp
using (var scope = app.ApplicationServices.CreateScope())
{
    var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    ragDb.Database.EnsureCreated();  // creates knowledge_chunks table + vector extension
}
```

This is acceptable for a sample. In production, use EF Core migrations instead.

---

## 9. Key Decisions and Trade-offs

### Why a separate RagDbContext?

XAF's `XafEFCoreDbContext` is heavily configured: it uses `TypesInfoInitializer`, deferred deletion, optimistic locking, and property access modes that are set up by XAF internally. The `UseVector()` call on the Npgsql data source must be applied at the data source level before the context is built; grafting this onto XAF's context configuration pipeline is fragile and poorly documented. A dedicated `RagDbContext` is simpler, explicit, and owned entirely by your RAG code.

### Why fire-and-forget Task.Run instead of Hangfire?

For a tutorial, `Task.Run` with a caught exception and a log entry is the simplest possible background execution. The trade-off: if the process crashes between `ObjectSpace.Committed` firing and the embedding call finishing, the chunk data is silently lost. For production use, replace `IngestionService` with a Hangfire job or a `Channel<T>`-backed hosted service so ingestion is durable and retriable.

### Why pure vector search instead of hybrid?

Cosine distance on PGVector is a single SQL query and straightforward to reason about. Hybrid search (combining BM25 full-text scores with vector scores, then re-ranking) significantly improves recall for keyword-heavy queries but adds an RRF or cross-encoder re-ranking step. Add it once pure vector search is working and you have a baseline to measure against.

### EF Core 8 compatibility constraints

`Pgvector.EntityFrameworkCore` version 0.2.x is the last series compatible with EF Core 8. Version 0.3.x requires EF Core 9. XAF 25.2.3 ships with EF Core 8, so stay on `Pgvector.EntityFrameworkCore` 0.2.2 until DevExpress ships an EF Core 9 compatible version of XAF. Similarly, `Npgsql.EntityFrameworkCore.PostgreSQL` must be 8.0.x to match EF Core 8.
