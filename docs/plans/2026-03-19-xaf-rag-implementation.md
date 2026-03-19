# XAF RAG Sample Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a tutorial-quality XAF Blazor Server app that demonstrates RAG (Retrieval-Augmented Generation) with PostgreSQL/PGVector and DevExpress DxAIChat.

**Architecture:** XAF manages KnowledgeArticle and Document entities via standard views. A separate RagDbContext handles the KnowledgeChunk table with vector(1536) embeddings. Services (chunking, embedding, retrieval) run in the Blazor Server process. DxAIChat intercepts user messages, queries similar chunks via cosine distance, augments the prompt, and streams GPT-4o responses.

**Tech Stack:** .NET 8, DevExpress XAF 25.2.3, EF Core 8, PostgreSQL + PGVector (Docker), OpenAI (text-embedding-3-small + gpt-4o), Microsoft.Extensions.AI, DxAIChat, Pgvector.EntityFrameworkCore

**Design Doc:** `docs/plans/2026-03-19-xaf-rag-sample-design.md`

---

### Task 1: Docker Infrastructure

**Files:**
- Create: `docker-compose.yml` (solution root)

**Step 1: Create docker-compose.yml**

```yaml
services:
  postgres:
    image: pgvector/pgvector:pg18
    environment:
      POSTGRES_DB: XafRag
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: password
    ports:
      - "5432:5432"
    volumes:
      - postgres-data:/var/lib/postgresql/data

volumes:
  postgres-data:
```

**Step 2: Start the container**

Run: `docker compose up -d`
Expected: Container starts, PostgreSQL listening on port 5432.

**Step 3: Verify PGVector extension is available**

Run: `docker exec -it xafrag-postgres-1 psql -U postgres -d XafRag -c "CREATE EXTENSION IF NOT EXISTS vector; SELECT extversion FROM pg_extension WHERE extname = 'vector';"`
Expected: Shows vector extension version (e.g., 0.8.0).

**Step 4: Commit**

```bash
git add docker-compose.yml
git commit -m "feat: add docker-compose with pgvector/pg18"
```

---

### Task 2: NuGet Packages

**Files:**
- Modify: `XafRag/XafRag.Module/XafRag.Module.csproj`
- Modify: `XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`

**Step 1: Add packages to the Module project**

Add to `XafRag.Module.csproj` `<ItemGroup>`:

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
```

**Step 2: Add packages to the Blazor Server project**

Add to `XafRag.Blazor.Server.csproj` `<ItemGroup>`:

```xml
<PackageReference Include="DevExpress.AIIntegration.Blazor.Chat" Version="25.2.4" />
<PackageReference Include="DevExpress.Document.Processor" Version="25.2.3" />
<PackageReference Include="Microsoft.Extensions.AI" Version="9.7.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.7.1-preview.1.25365.4" />
<PackageReference Include="Markdig" Version="0.42.0" />
<PackageReference Include="HtmlSanitizer" Version="8.1.870" />
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
```

**Step 3: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds with no errors.

**Step 4: Commit**

```bash
git add XafRag/XafRag.Module/XafRag.Module.csproj XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj
git commit -m "feat: add NuGet packages for RAG pipeline and DxAIChat"
```

---

### Task 3: Configuration

**Files:**
- Modify: `XafRag/XafRag.Blazor.Server/appsettings.json`
- Create: `XafRag/XafRag.Blazor.Server/Configuration/RagOptions.cs`
- Create: `XafRag/XafRag.Blazor.Server/Configuration/OpenAiOptions.cs`

**Step 1: Add OpenAI and RAG sections to appsettings.json**

Add after the `"AllowedHosts"` line:

```json
"OpenAI": {
  "EmbeddingModel": "text-embedding-3-small",
  "ChatModel": "gpt-4o"
},
"Rag": {
  "ChunkTokenLimit": 500,
  "ChunkOverlap": 100,
  "MaxResults": 5,
  "DistanceThreshold": 1.0
}
```

Note: The API key comes from the `OPENAI_API_KEY` environment variable — never stored in appsettings.

**Step 2: Create OpenAiOptions.cs**

```csharp
namespace XafRag.Blazor.Server.Configuration;

public class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o";
}
```

**Step 3: Create RagOptions.cs**

```csharp
namespace XafRag.Blazor.Server.Configuration;

public class RagOptions
{
    public const string SectionName = "Rag";
    public int ChunkTokenLimit { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxResults { get; set; } = 5;
    public double DistanceThreshold { get; set; } = 1.0;
}
```

**Step 4: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/appsettings.json XafRag/XafRag.Blazor.Server/Configuration/
git commit -m "feat: add OpenAI and RAG configuration options"
```

---

### Task 4: XAF Entities — KnowledgeArticle & Document

**Files:**
- Create: `XafRag/XafRag.Module/BusinessObjects/KnowledgeArticle.cs`
- Create: `XafRag/XafRag.Module/BusinessObjects/Document.cs`
- Create: `XafRag/XafRag.Module/BusinessObjects/DocumentStatus.cs`
- Modify: `XafRag/XafRag.Module/BusinessObjects/XafRagDbContext.cs`
- Modify: `XafRag/XafRag.Module/Module.cs`
- Modify: `XafRag/XafRag.Module/DatabaseUpdate/Updater.cs`

**Step 1: Create DocumentStatus enum**

```csharp
namespace XafRag.Module.BusinessObjects;

public enum DocumentStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
```

**Step 2: Create KnowledgeArticle entity**

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafRag.Module.BusinessObjects;

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

    [FieldSize(500)]
    public virtual string? Tags { get; set; }

    public virtual DateTime CreatedDate { get; set; }
    public virtual DateTime ModifiedDate { get; set; }

    public void OnCreated()
    {
        CreatedDate = DateTime.UtcNow;
        ModifiedDate = DateTime.UtcNow;
    }

    public void OnSaving()
    {
        ModifiedDate = DateTime.UtcNow;
    }

    public void OnLoaded() { }
}
```

**Step 3: Create Document entity**

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;

namespace XafRag.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DefaultProperty(nameof(FileName))]
public class Document : IXafEntityObject
{
    [Key]
    public virtual int Id { get; set; }

    [FieldSize(500)]
    public virtual string FileName { get; set; } = string.Empty;

    public virtual FileData? FileData { get; set; }

    public virtual DocumentStatus Status { get; set; }

    public virtual DateTime CreatedDate { get; set; }

    public void OnCreated()
    {
        CreatedDate = DateTime.UtcNow;
        Status = DocumentStatus.Pending;
    }

    public void OnSaving() { }
    public void OnLoaded() { }
}
```

**Step 4: Register entities in XafRagEFCoreDbContext**

Add to `XafRagDbContext.cs`, after the existing DbSet declarations:

```csharp
public DbSet<KnowledgeArticle> KnowledgeArticles { get; set; }
public DbSet<Document> Documents { get; set; }
```

**Step 5: Export types in Module.cs**

Add to the `XafRagModule()` constructor, before `RequiredModuleTypes`:

```csharp
AdditionalExportedTypes.Add(typeof(XafRag.Module.BusinessObjects.KnowledgeArticle));
AdditionalExportedTypes.Add(typeof(XafRag.Module.BusinessObjects.Document));
```

**Step 6: Add permissions in Updater.cs**

Add to the `CreateDefaultRole()` method, before the `return defaultRole;` line:

```csharp
defaultRole.AddTypePermissionsRecursively<KnowledgeArticle>(SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
defaultRole.AddTypePermissionsRecursively<Document>(SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
```

Add the using at the top of Updater.cs:
```csharp
using XafRag.Module.BusinessObjects;
```

Note: This using may already exist for `ApplicationUser`—check before adding a duplicate.

**Step 7: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 8: Commit**

```bash
git add XafRag/XafRag.Module/BusinessObjects/ XafRag/XafRag.Module/Module.cs XafRag/XafRag.Module/DatabaseUpdate/Updater.cs
git commit -m "feat: add KnowledgeArticle and Document XAF entities"
```

---

### Task 5: RagDbContext & KnowledgeChunk

KnowledgeChunk is NOT a XAF entity — it's internal to the RAG pipeline. It lives in a separate DbContext to cleanly support PGVector without interfering with XAF's ObjectSpace management.

**Files:**
- Create: `XafRag/XafRag.Module/BusinessObjects/KnowledgeChunk.cs`
- Create: `XafRag/XafRag.Module/BusinessObjects/ChunkSourceType.cs`
- Create: `XafRag/XafRag.Module/BusinessObjects/RagDbContext.cs`

**Step 1: Create ChunkSourceType enum**

```csharp
namespace XafRag.Module.BusinessObjects;

public enum ChunkSourceType
{
    Article,
    Document
}
```

**Step 2: Create KnowledgeChunk entity**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace XafRag.Module.BusinessObjects;

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
    public ChunkSourceType SourceType { get; set; }

    [Column("knowledge_article_id")]
    public int? KnowledgeArticleId { get; set; }

    [Column("document_id")]
    public int? DocumentId { get; set; }
}
```

**Step 3: Create RagDbContext**

```csharp
using Microsoft.EntityFrameworkCore;

namespace XafRag.Module.BusinessObjects;

public class RagDbContext : DbContext
{
    public RagDbContext(DbContextOptions<RagDbContext> options) : base(options) { }

    public DbSet<KnowledgeChunk> KnowledgeChunks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<KnowledgeChunk>(entity =>
        {
            entity.HasIndex(e => e.KnowledgeArticleId);
            entity.HasIndex(e => e.DocumentId);
        });
    }
}
```

**Step 4: Register RagDbContext in Startup.cs**

Add to `Startup.ConfigureServices`, after the `AppContext.SetSwitch` line:

```csharp
// RagDbContext for vector operations (separate from XAF's DbContext)
var ragConnectionString = Configuration.GetConnectionString("ConnectionString")!
    .Replace("EFCoreProvider=Postgres;", "");
var npgsqlDataSourceBuilder = new NpgsqlDataSourceBuilder(ragConnectionString);
npgsqlDataSourceBuilder.UseVector();
var npgsqlDataSource = npgsqlDataSourceBuilder.Build();

services.AddDbContext<XafRag.Module.BusinessObjects.RagDbContext>((sp, options) =>
{
    options.UseNpgsql(npgsqlDataSource, o => o.UseVector());
});
```

Add usings to Startup.cs:

```csharp
using Npgsql;
using XafRag.Module.BusinessObjects;
```

**Step 5: Add database initialization**

Add to `Startup.Configure`, after `app.UseXaf();`:

```csharp
// Ensure RAG database schema exists
using (var scope = app.ApplicationServices.CreateScope())
{
    var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    ragDb.Database.EnsureCreated();
}
```

Note: `EnsureCreated()` only creates tables that don't already exist. It won't conflict with XAF's tables since RagDbContext only maps `knowledge_chunks`.

**Step 6: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 7: Run the app and verify table creation**

Run: `dotnet run --project XafRag/XafRag.Blazor.Server`
Then verify: `docker exec -it xafrag-postgres-1 psql -U postgres -d XafRag -c "\dt knowledge_chunks"`
Expected: Table exists with vector column.

**Step 8: Commit**

```bash
git add XafRag/XafRag.Module/BusinessObjects/KnowledgeChunk.cs XafRag/XafRag.Module/BusinessObjects/ChunkSourceType.cs XafRag/XafRag.Module/BusinessObjects/RagDbContext.cs XafRag/XafRag.Blazor.Server/Startup.cs
git commit -m "feat: add RagDbContext with KnowledgeChunk and PGVector support"
```

---

### Task 6: ChunkingService

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Services/ChunkingService.cs`

**Step 1: Create ChunkingService**

```csharp
using Microsoft.Extensions.Options;
using XafRag.Blazor.Server.Configuration;

namespace XafRag.Blazor.Server.Services;

public class ChunkResult
{
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int ChunkIndex { get; set; }
}

public class ChunkingService
{
    private readonly RagOptions _options;

    public ChunkingService(IOptions<RagOptions> options)
    {
        _options = options.Value;
    }

    public List<ChunkResult> ChunkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var chunks = new List<ChunkResult>();
        var currentChunk = new List<string>();
        int currentTokens = 0;
        int chunkIndex = 0;

        foreach (var paragraph in paragraphs)
        {
            int paragraphTokens = EstimateTokens(paragraph);

            // If a single paragraph exceeds the limit, split it by sentences
            if (paragraphTokens > _options.ChunkTokenLimit)
            {
                // Flush current chunk first
                if (currentChunk.Count > 0)
                {
                    chunks.Add(CreateChunk(currentChunk, currentTokens, chunkIndex++));
                    currentChunk = GetOverlapParagraphs(currentChunk, _options.ChunkOverlap);
                    currentTokens = EstimateTokens(string.Join("\n\n", currentChunk));
                }

                // Split large paragraph by sentences
                var sentences = SplitIntoSentences(paragraph);
                foreach (var sentence in sentences)
                {
                    int sentenceTokens = EstimateTokens(sentence);
                    if (currentTokens + sentenceTokens > _options.ChunkTokenLimit && currentChunk.Count > 0)
                    {
                        chunks.Add(CreateChunk(currentChunk, currentTokens, chunkIndex++));
                        currentChunk = GetOverlapParagraphs(currentChunk, _options.ChunkOverlap);
                        currentTokens = EstimateTokens(string.Join("\n\n", currentChunk));
                    }
                    currentChunk.Add(sentence);
                    currentTokens += sentenceTokens;
                }
                continue;
            }

            if (currentTokens + paragraphTokens > _options.ChunkTokenLimit && currentChunk.Count > 0)
            {
                chunks.Add(CreateChunk(currentChunk, currentTokens, chunkIndex++));
                currentChunk = GetOverlapParagraphs(currentChunk, _options.ChunkOverlap);
                currentTokens = EstimateTokens(string.Join("\n\n", currentChunk));
            }

            currentChunk.Add(paragraph);
            currentTokens += paragraphTokens;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(CreateChunk(currentChunk, currentTokens, chunkIndex));
        }

        return chunks;
    }

    private static ChunkResult CreateChunk(List<string> paragraphs, int tokens, int index)
    {
        return new ChunkResult
        {
            Content = string.Join("\n\n", paragraphs),
            TokenCount = tokens,
            ChunkIndex = index
        };
    }

    private List<string> GetOverlapParagraphs(List<string> paragraphs, int overlapTokens)
    {
        var overlap = new List<string>();
        int tokens = 0;
        for (int i = paragraphs.Count - 1; i >= 0; i--)
        {
            tokens += EstimateTokens(paragraphs[i]);
            if (tokens > overlapTokens) break;
            overlap.Insert(0, paragraphs[i]);
        }
        return overlap;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting on period, question mark, exclamation mark
        var sentences = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '?' or '!' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]))
            {
                sentences.Add(text[start..(i + 1)].Trim());
                start = i + 2;
            }
        }
        if (start < text.Length)
            sentences.Add(text[start..].Trim());
        return sentences.Where(s => s.Length > 0).ToList();
    }

    /// <summary>
    /// Rough token estimate: ~4 characters per token for English text.
    /// Good enough for chunking; exact counts aren't critical.
    /// </summary>
    public static int EstimateTokens(string text) => text.Length / 4;
}
```

**Step 2: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Services/ChunkingService.cs
git commit -m "feat: add ChunkingService with paragraph-aware token splitting"
```

---

### Task 7: EmbeddingService

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Services/EmbeddingService.cs`

**Step 1: Create EmbeddingService**

```csharp
using Microsoft.Extensions.AI;
using Pgvector;

namespace XafRag.Blazor.Server.Services;

public class EmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        ILogger<EmbeddingService> logger)
    {
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
    }

    public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var result = await _embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
        return new Vector(result[0].Vector);
    }

    public async Task<List<Vector>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        _logger.LogInformation("Generating embeddings for {Count} chunks", texts.Count);

        var result = await _embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);
        return result.Select(e => new Vector(e.Vector)).ToList();
    }
}
```

**Step 2: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Services/EmbeddingService.cs
git commit -m "feat: add EmbeddingService wrapping IEmbeddingGenerator"
```

---

### Task 8: DocumentProcessingService

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Services/DocumentProcessingService.cs`

**Step 1: Create DocumentProcessingService**

This service extracts plain text from uploaded files. It uses DevExpress Document Processor for PDF and DOCX, and direct reading for TXT/MD.

```csharp
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
            using var stream = new MemoryStream(fileBytes);
            using var processor = new RichEditDocumentServer();
            processor.LoadDocument(stream, DocumentFormat.Pdf);
            return processor.Document.GetText(processor.Document.Range);
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
```

**Step 2: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds. If `RichEditDocumentServer` is not available, check if `DevExpress.Document.Processor` was installed. If the API differs, adapt to the available DevExpress document API. The `DevExpress.Document.Processor` package provides `RichEditDocumentServer` for server-side document processing.

**Step 3: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Services/DocumentProcessingService.cs
git commit -m "feat: add DocumentProcessingService for PDF/DOCX/TXT extraction"
```

---

### Task 9: RagService

The orchestrator: embeds user queries, searches for similar chunks, builds augmented prompts, and calls GPT-4o.

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Services/RagService.cs`

**Step 1: Create RagService**

```csharp
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

        // Include conversation history if provided
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
```

**Step 2: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Services/RagService.cs
git commit -m "feat: add RagService with vector search and streaming LLM responses"
```

---

### Task 10: Ingestion Service & Controllers

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Services/IngestionService.cs`
- Create: `XafRag/XafRag.Blazor.Server/Controllers/KnowledgeArticleIngestionController.cs`
- Create: `XafRag/XafRag.Blazor.Server/Controllers/DocumentIngestionController.cs`

**Step 1: Create IngestionService**

This service handles the chunking + embedding + storage pipeline. It's called from XAF ViewControllers.

```csharp
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

                // Delete existing chunks for this article
                await ragDb.KnowledgeChunks
                    .Where(c => c.KnowledgeArticleId == articleId)
                    .ExecuteDeleteAsync();

                // Chunk the content
                var chunks = chunkingService.ChunkText(content);
                if (chunks.Count == 0) return;

                // Generate embeddings
                var texts = chunks.Select(c => c.Content).ToList();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);

                // Store chunks with embeddings
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

                // Extract text from file
                var text = docProcessor.ExtractText(fileBytes, fileName);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("No text extracted from document {Id}", documentId);
                    return;
                }

                // Delete existing chunks for this document
                await ragDb.KnowledgeChunks
                    .Where(c => c.DocumentId == documentId)
                    .ExecuteDeleteAsync();

                // Chunk the content
                var chunks = chunkingService.ChunkText(text);
                if (chunks.Count == 0) return;

                // Generate embeddings
                var texts = chunks.Select(c => c.Content).ToList();
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(texts);

                // Store chunks with embeddings
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
```

**Step 2: Create KnowledgeArticleIngestionController**

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class KnowledgeArticleIngestionController : ObjectViewController<DetailView, KnowledgeArticle>
{
    private IngestionService? _ingestionService;

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
```

**Step 3: Create DocumentIngestionController**

```csharp
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
            // Read file bytes from XAF FileData
            using var ms = new MemoryStream();
            doc.FileData.SaveToStream(ms);
            var bytes = ms.ToArray();

            doc.Status = DocumentStatus.Processing;
            _ingestionService?.IngestDocumentInBackground(doc.Id, doc.FileName, bytes);
        }
    }
}
```

**Step 4: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Services/IngestionService.cs XafRag/XafRag.Blazor.Server/Controllers/
git commit -m "feat: add ingestion pipeline with XAF ViewControllers for articles and documents"
```

---

### Task 11: OpenAI & Service Registration in Startup.cs

**Files:**
- Modify: `XafRag/XafRag.Blazor.Server/Startup.cs`

**Step 1: Add all service registrations**

Add usings at the top of Startup.cs:

```csharp
using DevExpress.AIIntegration;
using Microsoft.Extensions.AI;
using OpenAI;
using XafRag.Blazor.Server.Configuration;
using XafRag.Blazor.Server.Services;
```

Add to `ConfigureServices`, after the RagDbContext registration (added in Task 5) and before `services.AddXaf(...)`:

```csharp
// Configuration
services.Configure<OpenAiOptions>(Configuration.GetSection(OpenAiOptions.SectionName));
services.Configure<RagOptions>(Configuration.GetSection(RagOptions.SectionName));

// OpenAI clients
var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
var openAiOptions = Configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>()!;
var openAiClient = new OpenAIClient(openAiApiKey);

// Chat client (for DxAIChat and RagService)
IChatClient chatClient = openAiClient
    .GetChatClient(openAiOptions.ChatModel)
    .AsIChatClient();
services.AddChatClient(chatClient);

// Embedding generator
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = openAiClient
    .GetEmbeddingClient(openAiOptions.EmbeddingModel)
    .AsEmbeddingGenerator();
services.AddSingleton(embeddingGenerator);

// DevExpress AI services
services.AddDevExpressBlazor();
services.AddDevExpressAI();

// RAG services
services.AddScoped<ChunkingService>();
services.AddScoped<EmbeddingService>();
services.AddScoped<DocumentProcessingService>();
services.AddScoped<RagService>();
services.AddSingleton<IngestionService>();
```

Note: `AddDevExpressBlazor()` may already be called elsewhere — check Startup.cs and only add it if it's not already present.

**Step 2: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 3: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Startup.cs
git commit -m "feat: register OpenAI clients, DevExpress AI, and RAG services"
```

---

### Task 12: Chat UI — DxAIChat in XAF

We embed DxAIChat in XAF using a custom ViewItem rendered in a DetailView. The user navigates to "RAG Chat" in the Knowledge Base navigation group.

**Files:**
- Create: `XafRag/XafRag.Blazor.Server/Editors/RagChatComponent.razor`
- Create: `XafRag/XafRag.Blazor.Server/Editors/RagChatViewItem.cs`
- Create: `XafRag/XafRag.Module/BusinessObjects/RagChatHolder.cs`
- Create: `XafRag/XafRag.Blazor.Server/Controllers/RagChatWindowController.cs`

**Step 1: Create the non-persistent object for the view**

```csharp
using System.ComponentModel;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafRag.Module.BusinessObjects;

[DomainComponent]
[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DisplayName("RAG Chat")]
public class RagChatHolder : NonPersistentObjectImpl
{
}
```

Register in Module.cs constructor:

```csharp
AdditionalExportedTypes.Add(typeof(XafRag.Module.BusinessObjects.RagChatHolder));
```

**Step 2: Create the Blazor component**

Create `XafRag/XafRag.Blazor.Server/Editors/RagChatComponent.razor`:

```razor
@using DevExpress.AIIntegration.Blazor.Chat
@using Markdig
@using Ganss.Xss
@inject XafRag.Blazor.Server.Services.RagService RagService

<DxAIChat CssClass="rag-chat"
          UseStreaming="false"
          ResponseContentFormat="ResponseContentFormat.Markdown"
          MessageSent="OnMessageSent"
          Initialized="OnInitialized">
    <MessageContentTemplate>
        <div class="rag-chat-content">
            @ToHtml(context.Content)
        </div>
    </MessageContentTemplate>
    <EmptyMessageAreaTemplate>
        <div class="rag-chat-empty">
            <h3>Knowledge Base Assistant</h3>
            <p>Ask questions about your knowledge base. I'll search for relevant information and provide answers based on your articles and documents.</p>
        </div>
    </EmptyMessageAreaTemplate>
</DxAIChat>

<style>
    .rag-chat {
        width: 100%;
        height: calc(100vh - 200px);
        min-height: 400px;
    }
    .rag-chat-content > p:last-child {
        margin-bottom: 0;
    }
    .rag-chat-empty {
        text-align: center;
        padding: 40px;
        color: #666;
    }
</style>

@code {
    private readonly HtmlSanitizer _sanitizer = new();

    private void OnInitialized(IAIChat chat)
    {
        // No system prompt loaded here — RagService manages the prompt
    }

    private async Task OnMessageSent(MessageSentEventArgs args)
    {
        var question = args.Content;
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in RagService.AskAsync(question, cancellationToken: args.CancellationToken))
        {
            responseBuilder.Append(chunk);
        }

        await args.Chat.SendMessage(responseBuilder.ToString(), ChatRole.Assistant);
    }

    private MarkupString ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return new MarkupString(string.Empty);

        var html = Markdown.ToHtml(markdown);
        html = _sanitizer.Sanitize(html);
        return new MarkupString(html);
    }
}
```

**Step 3: Create the ViewItem adapter**

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;

namespace XafRag.Blazor.Server.Editors;

public interface IModelRagChatViewItem : IModelViewItem { }

[ViewItem(typeof(IModelRagChatViewItem))]
public class RagChatViewItem : ViewItem, IComplexViewItem
{
    private XafApplication? _application;
    private IObjectSpace? _objectSpace;

    public RagChatViewItem(IModelViewItem model, Type objectType)
        : base(objectType, model.Id) { }

    public void Setup(IObjectSpace objectSpace, XafApplication application)
    {
        _application = application;
        _objectSpace = objectSpace;
    }

    protected override object CreateControlCore()
    {
        return new ComponentAdapter(new RagChatComponentModel());
    }
}

public class RagChatComponentModel : ComponentModelBase
{
    public override Type ComponentType => typeof(RagChatComponent);
}
```

**Step 4: Create the WindowController for navigation**

This controller configures the RagChatHolder DetailView to show our custom ViewItem instead of the default empty view.

```csharp
using DevExpress.ExpressApp;
using XafRag.Module.BusinessObjects;

namespace XafRag.Blazor.Server.Controllers;

public class RagChatWindowController : ObjectViewController<DetailView, RagChatHolder>
{
    protected override void OnActivated()
    {
        base.OnActivated();
        // The view is configured via Model Editor to show RagChatViewItem
    }
}
```

Note: After first run, use XAF's Model Editor to configure the `RagChatHolder_DetailView`:
1. Remove the default layout items
2. Add a `RagChatViewItem` using the IModelRagChatViewItem interface
3. Set it to fill the entire view area

Alternatively, configure the view programmatically in a ModelDifference or via the `Model.DesignedDiffs.xafml` file. The exact Model Editor steps depend on the XAF version — check the XAF docs for "Custom View Item" for the correct procedure.

**Step 5: Verify build**

Run: `dotnet build XafRag/XafRag.Blazor.Server/XafRag.Blazor.Server.csproj`
Expected: Build succeeds.

**Step 6: Commit**

```bash
git add XafRag/XafRag.Blazor.Server/Editors/ XafRag/XafRag.Blazor.Server/Controllers/RagChatWindowController.cs XafRag/XafRag.Module/BusinessObjects/RagChatHolder.cs XafRag/XafRag.Module/Module.cs
git commit -m "feat: add DxAIChat UI integrated as XAF custom ViewItem"
```

---

### Task 13: End-to-End Verification

**Step 1: Ensure Docker is running**

Run: `docker compose up -d`

**Step 2: Set OpenAI API key**

Run: `export OPENAI_API_KEY=sk-your-key-here` (or set via Windows environment variables)

**Step 3: Run the application**

Run: `dotnet run --project XafRag/XafRag.Blazor.Server`

**Step 4: Manual test checklist**

1. Log in as Admin (empty password)
2. Navigate to Knowledge Base > Knowledge Article
3. Create a new article with title "XAF Overview" and content about XAF (a few paragraphs)
4. Save — check logs for "Ingesting article" and "Ingested N chunks" messages
5. Navigate to Knowledge Base > RAG Chat
6. Ask: "What is XAF?" — verify the response references the article content
7. Navigate to Knowledge Base > Document
8. Upload a small PDF or DOCX file
9. Save — check logs for document ingestion
10. Return to RAG Chat and ask about the document's content

**Step 5: Commit any fixes**

If any fixes are needed during verification, commit them.

**Step 6: Final commit**

```bash
git add -A
git commit -m "feat: XAF RAG sample solution — complete implementation"
```

---

## Summary

| Task | Description | Key Files |
|------|-------------|-----------|
| 1 | Docker + PGVector | `docker-compose.yml` |
| 2 | NuGet packages | Both `.csproj` files |
| 3 | Configuration | `appsettings.json`, `Configuration/*.cs` |
| 4 | XAF entities | `KnowledgeArticle.cs`, `Document.cs` |
| 5 | RagDbContext + KnowledgeChunk | `RagDbContext.cs`, `KnowledgeChunk.cs` |
| 6 | ChunkingService | `Services/ChunkingService.cs` |
| 7 | EmbeddingService | `Services/EmbeddingService.cs` |
| 8 | DocumentProcessingService | `Services/DocumentProcessingService.cs` |
| 9 | RagService | `Services/RagService.cs` |
| 10 | Ingestion pipeline + controllers | `Services/IngestionService.cs`, `Controllers/*.cs` |
| 11 | Startup registration | `Startup.cs` |
| 12 | Chat UI (DxAIChat) | `Editors/RagChat*.cs`, `Editors/RagChatComponent.razor` |
| 13 | End-to-end verification | Manual testing |
