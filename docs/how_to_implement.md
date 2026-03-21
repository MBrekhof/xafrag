# How to Add RAG to Your Own XAF Application

This guide walks you through adding Retrieval-Augmented Generation (RAG) to an existing DevExpress XAF Blazor Server application. It covers every layer — from database setup to chat UI — using the patterns from the XafRag sample.

For the complete source code of every file mentioned here, refer to this repository.

---

## Prerequisites

- .NET 8 SDK with a working XAF Blazor Server project (DevExpress 25.2.x)
- Docker (for PostgreSQL + PGVector)
- An OpenAI API key
- DevExpress NuGet feed configured

---

## Step 1: Add Docker for PGVector

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
      - "5435:5432"
    volumes:
      - postgres-data:/var/lib/postgresql

volumes:
  postgres-data:
```

Note: PGVector pg18 changed its data directory layout. Mount at `/var/lib/postgresql` (not `/var/lib/postgresql/data`).

```bash
docker compose up -d
```

---

## Step 2: Install NuGet Packages

### Module project (.Module.csproj)

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
```

### Blazor Server project (.Blazor.Server.csproj)

```xml
<PackageReference Include="Pgvector" Version="0.3.2" />
<PackageReference Include="Pgvector.EntityFrameworkCore" Version="0.3.0" />
<PackageReference Include="Microsoft.Extensions.AI" Version="9.7.1" />
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="9.7.1-preview.1.25365.4" />
<PackageReference Include="DevExpress.AIIntegration.Blazor.Chat" Version="25.2.4" />
<PackageReference Include="DevExpress.Document.Processor" Version="25.2.3" />
<PackageReference Include="Markdig" Version="0.42.0" />
<PackageReference Include="HtmlSanitizer" Version="8.1.870" />
<PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
```

---

## Step 3: Configuration

### appsettings.json

Add the OpenAI and RAG sections (keep `ApiKey` empty here):

```json
"OpenAI": {
  "ApiKey": "",
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

### appsettings.Development.json (gitignored)

Place your actual API key here:

```json
{
  "OpenAI": {
    "ApiKey": "sk-your-key-here"
  }
}
```

Make sure `appsettings.Development.json` is in your `.gitignore`.

### Options classes

```csharp
// Configuration/OpenAiOptions.cs
public class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o";
}

// Configuration/RagOptions.cs
public class RagOptions
{
    public const string SectionName = "Rag";
    public int ChunkTokenLimit { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxResults { get; set; } = 5;
    public double DistanceThreshold { get; set; } = 1.0;
}
```

---

## Step 4: Data Model

### XAF entities (Module project)

**KnowledgeArticle** — a standard XAF entity for manually authored content:

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

**Document** — uses XAF's `FileData` for binary uploads. The filename is auto-populated from the uploaded file:

```csharp
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

    public void OnSaving()
    {
        if (FileData != null && !string.IsNullOrEmpty(FileData.FileName))
            FileName = FileData.FileName;
    }

    public void OnLoaded() { }
}

public enum DocumentStatus { Pending, Processing, Completed, Failed }
```

**RagChatHolder** — a non-persistent placeholder. XAF needs a business object to open a DetailView, even when there is no database record:

```csharp
[DomainComponent]
[DefaultClassOptions]
[NavigationItem("Knowledge Base")]
[DisplayName("RAG Chat")]
public class RagChatHolder : NonPersistentBaseObject { }
```

Register `KnowledgeArticle` and `Document` as `DbSet` properties in your `XafEFCoreDbContext`. Do **not** register `RagChatHolder` — it is non-persistent.

### KnowledgeChunk and RagDbContext (Module project)

`KnowledgeChunk` stores text chunks and their embeddings. It uses standard EF Core annotations — XAF must not manage this entity:

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
    public ChunkSourceType SourceType { get; set; }

    [Column("knowledge_article_id")]
    public int? KnowledgeArticleId { get; set; }

    [Column("document_id")]
    public int? DocumentId { get; set; }
}

public enum ChunkSourceType { Article, Document }
```

The dimension `vector(1536)` matches `text-embedding-3-small`. Use `vector(3072)` for `text-embedding-3-large`.

`RagDbContext` is a standalone `DbContext` completely separate from XAF's context:

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

**Why a separate DbContext?** XAF's EF Core context uses change-tracking proxies, deferred deletion, and optimistic locking that are incompatible with PGVector's raw vector operations. A dedicated `RagDbContext` avoids all of that friction.

---

## Step 5: Services

All services go in the Blazor Server project under `Services/`.

### ChunkingService

Splits text into overlapping chunks at paragraph boundaries. Paragraphs exceeding the token limit are split further at sentence boundaries. Token count is estimated as `text.Length / 4`. Default: 500 tokens per chunk, 100-token overlap.

### EmbeddingService

Wraps `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI`:

```csharp
public async Task<Vector> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
{
    var result = await _embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
    return new Vector(result[0].Vector);
}
```

### DocumentProcessingService

Extracts plain text from uploaded files using DevExpress document processors:

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

Combines vector search with LLM chat. Key features:

- **Source name resolution**: after the vector search, the service queries the `Documents` and `KnowledgeArticles` tables to resolve actual filenames/titles
- **Context formatting**: chunks are labeled as `**[Part N of "filename.md"]**` so the LLM can cite sources with bold references
- **Streaming**: `AskAsync` returns `IAsyncEnumerable<string>` for token-by-token rendering

```csharp
var contextText = searchResults.Count > 0
    ? string.Join("\n\n---\n\n", searchResults.Select(r =>
        $"**[Part {r.ChunkIndex + 1} of \"{r.SourceName}\"]** {r.Content}"))
    : "No relevant context found in the knowledge base.";
```

### IngestionService

Runs ingestion on a background thread via `Task.Run`. Each invocation creates its own DI scope. After completion, it updates the Document status to `Completed` or `Failed` via direct SQL:

```csharp
private static async Task UpdateDocumentStatus(RagDbContext ragDb, int documentId, DocumentStatus status)
{
    await ragDb.Database.ExecuteSqlRawAsync(
        """UPDATE "Documents" SET "Status" = {0} WHERE "Id" = {1}""",
        (int)status, documentId);
}
```

---

## Step 6: Ingestion Controllers

### DocumentIngestionController

Key patterns:
- **Re-entrancy guard**: prevents infinite loop when `CommitChanges()` inside `Committed` fires `Committed` again
- **Status check**: only processes documents in `Pending` status
- **Filename fallback**: uses `FileData.FileName` if `Document.FileName` is empty

```csharp
public class DocumentIngestionController : ObjectViewController<DetailView, Document>
{
    private bool _isCommitting;

    private void ObjectSpace_Committed(object? sender, EventArgs e)
    {
        if (_isCommitting) return;

        var doc = ViewCurrentObject;
        if (doc?.FileData == null || doc.Status != DocumentStatus.Pending) return;

        using var ms = new MemoryStream();
        doc.FileData.SaveToStream(ms);
        var bytes = ms.ToArray();
        if (bytes.Length == 0) return;

        var fileName = !string.IsNullOrEmpty(doc.FileName)
            ? doc.FileName : doc.FileData.FileName;

        _isCommitting = true;
        try
        {
            doc.Status = DocumentStatus.Processing;
            ObjectSpace.CommitChanges();
        }
        finally { _isCommitting = false; }

        _ingestionService?.IngestDocumentInBackground(doc.Id, fileName, bytes);
    }
}
```

Create a matching `KnowledgeArticleIngestionController` for articles — it is simpler since there is no file extraction or status tracking.

---

## Step 7: Chat UI

### RagChatComponent.razor

The Razor component wraps `DxAIChat` with manual message handling (bypassing the built-in AI client to inject RAG context):

```razor
@inject RagService RagService

<DxAIChat CssClass="rag-chat"
          ShowHeader="true"
          HeaderText="Knowledge Base Assistant"
          UseStreaming="false"
          ResponseContentFormat="ResponseContentFormat.Markdown"
          MessageSent="OnMessageSent">
    <MessageContentTemplate>
        <div class="rag-chat-content">
            @ToHtml(context.Content)
        </div>
    </MessageContentTemplate>
    <EmptyMessageAreaTemplate>
        <div class="rag-chat-empty">
            <h3>Knowledge Base Assistant</h3>
            <p>Ask questions about your knowledge base.</p>
        </div>
    </EmptyMessageAreaTemplate>
</DxAIChat>

@code {
    private readonly HtmlSanitizer _sanitizer = new();

    private async Task OnMessageSent(MessageSentEventArgs args)
    {
        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in RagService.AskAsync(args.Content, ct: args.CancellationToken))
            sb.Append(chunk);
        await args.Chat.SendMessage(sb.ToString(), ChatRole.Assistant);
    }

    private MarkupString ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return new MarkupString(string.Empty);
        var html = Markdown.ToHtml(markdown);
        return new MarkupString(_sanitizer.Sanitize(html));
    }
}
```

`ShowHeader="true"` enables the built-in **Clear Chat** button.

### RagChatViewItem

XAF Blazor renders custom UI through `IComponentContentHolder`:

```csharp
public interface IModelRagChatViewItem : IModelViewItem { }

[ViewItem(typeof(IModelRagChatViewItem))]
public class RagChatViewItem(IModelViewItem model, Type objectType)
    : ViewItem(objectType, model.Id),
      IComponentContentHolder, IComplexViewItem
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

### RagChatDetailViewUpdater

Programmatically adds the ViewItem to the DetailView layout so you don't need to use the Model Editor:

```csharp
public class RagChatDetailViewUpdater : ModelNodesGeneratorUpdater<ModelViewsNodesGenerator>
{
    public override void UpdateNode(ModelNode node)
    {
        var views = (IModelViews)node;
        if (views["RagChatHolder_DetailView"] is not IModelDetailView dv) return;

        const string chatItemId = "RagChatItem";
        if (dv.Items[chatItemId] == null)
            dv.Items.AddNode<IModelRagChatViewItem>(chatItemId);

        // Remove the default Oid property editor
        var oidItem = dv.Items["Oid"];
        if (oidItem != null) ((IModelNode)oidItem).Remove();

        // Rebuild layout with only the chat item
        var layout = dv.Layout;
        if (layout == null) return;
        for (int i = layout.Count - 1; i >= 0; i--)
            layout[i].Remove();

        var chatLayoutItem = layout.AddNode<IModelLayoutViewItem>(chatItemId);
        chatLayoutItem.ViewItem = (IModelViewItem)dv.Items[chatItemId];
    }
}
```

Register it in your Blazor module:

```csharp
public override void AddGeneratorUpdaters(ModelNodesGeneratorUpdaters updaters)
{
    base.AddGeneratorUpdaters(updaters);
    updaters.Add(new RagChatDetailViewUpdater());
}
```

### RagChatWindowController

Since `RagChatHolder` is non-persistent, XAF generates a ListView by default. This controller intercepts navigation and redirects to the DetailView:

```csharp
public class RagChatWindowController : WindowController
{
    public RagChatWindowController() { TargetWindowType = WindowType.Main; }

    protected override void OnActivated()
    {
        base.OnActivated();
        var navController = Frame.GetController<ShowNavigationItemController>();
        if (navController != null)
            navController.CustomShowNavigationItem += OnCustomShowNavigationItem;
    }

    protected override void OnDeactivated()
    {
        var navController = Frame.GetController<ShowNavigationItemController>();
        if (navController != null)
            navController.CustomShowNavigationItem -= OnCustomShowNavigationItem;
        base.OnDeactivated();
    }

    private void OnCustomShowNavigationItem(object? sender, CustomShowNavigationItemEventArgs e)
    {
        if (e.ActionArguments.SelectedChoiceActionItem?.Data is ViewShortcut shortcut
            && shortcut.ViewId == "RagChatHolder_ListView")
        {
            var objectSpace = Application.CreateObjectSpace(typeof(RagChatHolder));
            var holder = objectSpace.CreateObject<RagChatHolder>();
            var detailView = Application.CreateDetailView(objectSpace, holder);
            detailView.ViewEditMode = ViewEditMode.View;
            e.ActionArguments.ShowViewParameters.CreatedView = detailView;
            e.Handled = true;
        }
    }
}
```

**Important:** Do not use `Frame.SetView()` — it disposes the ListView while Blazor is still rendering it. Always use `ShowViewParameters.CreatedView` via `CustomShowNavigationItem`.

---

## Step 8: Register Everything in Startup.cs

Add before `AddXaf`:

```csharp
// 1. RagDbContext with PGVector
var ragConnStr = Configuration.GetConnectionString("ConnectionString")!
    .Replace("EFCoreProvider=Postgres;", "");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(ragConnStr);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

services.AddDbContext<RagDbContext>((sp, options) =>
    options.UseNpgsql(dataSource, o => o.UseVector()));

// 2. Configuration
services.Configure<OpenAiOptions>(Configuration.GetSection("OpenAI"));
services.Configure<RagOptions>(Configuration.GetSection("Rag"));

// 3. OpenAI clients
var openAiOptions = Configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>()!;
var openAiClient = new OpenAIClient(openAiOptions.ApiKey);

IChatClient chatClient = openAiClient
    .GetChatClient(openAiOptions.ChatModel).AsIChatClient();
services.AddChatClient(chatClient);

IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = openAiClient
    .GetEmbeddingClient(openAiOptions.EmbeddingModel).AsIEmbeddingGenerator();
services.AddSingleton(embeddingGenerator);

// 4. DevExpress AI
services.AddDevExpressAI();

// 5. RAG services
services.AddScoped<ChunkingService>();
services.AddScoped<EmbeddingService>();
services.AddScoped<DocumentProcessingService>();
services.AddScoped<RagService>();
services.AddSingleton<IngestionService>();
```

After `UseXaf()` in `Configure`, ensure the vector table is created:

```csharp
using (var scope = app.ApplicationServices.CreateScope())
{
    var ragDb = scope.ServiceProvider.GetRequiredService<RagDbContext>();
    ragDb.Database.EnsureCreated();
}
```

### Serilog setup (Program.cs)

```csharp
.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console()
        .WriteTo.File("logs/xafrag-.log",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
})
```

---

## Step 9: Security Permissions

In your `Updater.cs`, grant the default role access to the new entities:

```csharp
defaultRole.AddTypePermissionsRecursively<KnowledgeArticle>(
    SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
defaultRole.AddTypePermissionsRecursively<Document>(
    SecurityOperations.CRUDAccess, SecurityPermissionState.Allow);
```

---

## Key Decisions and Gotchas

### Why a separate RagDbContext?

XAF's EF Core context uses change-tracking proxies, deferred deletion, and optimistic locking. PGVector requires `UseVector()` on the `NpgsqlDataSource` before the context is built. Mixing these in XAF's context pipeline is fragile. A dedicated `RagDbContext` is simpler and fully owned by your RAG code.

### Document status tracking

The `IngestionService` runs on a background thread without access to XAF's `ObjectSpace`. It updates the `Document.Status` column via direct SQL through `RagDbContext.Database.ExecuteSqlRawAsync`. This avoids creating an `ObjectSpace` from a background thread (which requires careful security context handling).

### Re-entrancy in ObjectSpace.Committed

When the `DocumentIngestionController` sets `Status = Processing` and calls `ObjectSpace.CommitChanges()` inside the `Committed` handler, it fires `Committed` again. Use a boolean `_isCommitting` guard to prevent infinite recursion.

### ListView → DetailView redirect for non-persistent objects

XAF generates a ListView for `RagChatHolder` by default. Do not use `Frame.SetView()` to redirect — it disposes the ListView while Blazor is still rendering, causing an `ObjectDisposedException`. Instead, use `CustomShowNavigationItem` and set `ShowViewParameters.CreatedView`.

### PGVector pg18 volume mount

PGVector pg18 changed its data directory layout. Mount volumes at `/var/lib/postgresql` (not `/var/lib/postgresql/data`) or the container will exit with an error about incompatible data formats.

### Fire-and-forget vs durable jobs

`Task.Run` with exception logging is the simplest background execution. If the process crashes mid-ingestion, chunk data is silently lost. For production, replace with Hangfire or a `Channel<T>`-backed hosted service.
