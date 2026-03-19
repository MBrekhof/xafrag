# XAF RAG Sample Solution — Design Document

**Date:** 2026-03-19
**Purpose:** Tutorial/reference implementation showcasing RAG in a DevExpress XAF Blazor Server app with PostgreSQL + PGVector.

## Decisions

- **Approach:** DevExpress `DxAIChat` for UI + custom RAG pipeline for retrieval
- **LLM Provider:** OpenAI (text-embedding-3-small for embeddings, GPT-4o for chat)
- **Vector Store:** PostgreSQL with PGVector extension via Docker
- **Chunking:** Simple token-based (~500 tokens, ~100 overlap), paragraph-aware
- **Background Processing:** Fire-and-forget `Task.Run` (no Hangfire)
- **Search:** Pure cosine similarity; hybrid search noted as extension
- **Chat UI:** `DxAIChat` with streaming + Markdown rendering; scope flexible (chat view vs minimal panel, decided later)

## Architecture

```
┌─────────────────────────────────────────────────────┐
│ XafRag.Blazor.Server                                │
│                                                     │
│  ┌──────────┐   ┌──────────────┐   ┌─────────────┐ │
│  │ DxAIChat  │──▶│  RagService  │──▶│ EmbeddingSvc│ │
│  │ (UI)     │   │  (orchestr.) │   │ (OpenAI)    │ │
│  └──────────┘   └──────┬───────┘   └─────────────┘ │
│                        │                            │
│  ┌─────────────────────▼──────────────────────────┐ │
│  │            DocumentProcessingService           │ │
│  │  (DevExpress DocumentProcessor + ChunkingSvc)  │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────┬───────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────┐
│ XafRag.Module                                       │
│                                                     │
│  Entities: KnowledgeArticle, Document,              │
│            KnowledgeChunk (vector(1536))             │
│                                                     │
│  DbContext: XafRagEFCoreDbContext + UseVector()      │
└─────────────────────────┬───────────────────────────┘
                          │
              ┌───────────▼────────────┐
              │ PostgreSQL + PGVector  │
              │ (Docker)              │
              └────────────────────────┘
```

## Data Model

### KnowledgeArticle
| Column       | Type     | Notes                    |
|-------------|----------|--------------------------|
| Id          | int (PK) | Auto-increment           |
| Title       | string   |                          |
| Content     | string   | Unlimited text           |
| Tags        | string?  | Comma-separated          |
| CreatedDate | DateTime |                          |
| ModifiedDate| DateTime |                          |

### Document
| Column      | Type              | Notes                         |
|------------|-------------------|-------------------------------|
| Id         | int (PK)          | Auto-increment                |
| FileName   | string            |                               |
| FileData   | XAF FileData      | Binary storage                |
| Status     | enum              | Pending/Processing/Completed/Failed |
| CreatedDate| DateTime          |                               |

### KnowledgeChunk
| Column              | Type         | Notes                          |
|--------------------|--------------|--------------------------------|
| Id                 | int (PK)     | Auto-increment                 |
| Content            | string       | Chunk text                     |
| Embedding          | vector(1536) | PGVector column                |
| TokenCount         | int          |                                |
| ChunkIndex         | int          | Position within source         |
| SourceType         | enum         | Article / Document             |
| KnowledgeArticleId | int? (FK)    | Nullable                       |
| DocumentId         | int? (FK)    | Nullable                       |

## Ingestion Pipeline

1. User saves `KnowledgeArticle` or uploads `Document` via XAF UI
2. `ObjectSpace.Committed` triggers processing
3. For documents: `DocumentProcessingService` extracts text (DevExpress `DocumentProcessor` for PDF/DOCX, raw UTF-8 for TXT/MD)
4. `ChunkingService` splits into ~500-token chunks with ~100-token overlap on paragraph boundaries
5. `EmbeddingService` calls OpenAI `text-embedding-3-small` (batched)
6. Chunks + embeddings persisted to `KnowledgeChunk`
7. Re-saving a source deletes old chunks and regenerates

## Retrieval Pipeline

1. `DxAIChat.MessageSent` intercepts user message
2. `RagService.SearchAsync(query)`:
   - Embed query via `text-embedding-3-small`
   - Raw SQL: cosine distance (`<=>`) against `KnowledgeChunk.Embedding`
   - Return top 5 chunks within distance threshold (<= 1.0)
3. Build augmented prompt with system message + retrieved context + user question
4. Stream response from GPT-4o back through `DxAIChat`

## Chat UI

- `DxAIChat` with `UseStreaming="true"`, `ResponseContentFormat="Markdown"`
- `MessageContentTemplate` with Markdig + HtmlSanitizer
- Prompt suggestions for common queries
- System prompt instructs the model to use provided context and cite sources

## Infrastructure

### Docker (docker-compose.yml)
- `pgvector/pgvector:pg18` on port 5432
- Persistent volume for data

### Configuration (appsettings.json)
- `OpenAI:ApiKey` from `OPENAI_API_KEY` env var
- `OpenAI:EmbeddingModel`: `text-embedding-3-small`
- `OpenAI:ChatModel`: `gpt-4o`
- `Rag:ChunkTokenLimit`: 500
- `Rag:ChunkOverlap`: 100
- `Rag:MaxResults`: 5
- `Rag:DistanceThreshold`: 1.0

### NuGet Packages to Add
- `Pgvector` + `Pgvector.EntityFrameworkCore`
- `DevExpress.AIIntegration.Blazor.Chat`
- `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI`
- `Markdig` + `HtmlSanitizer`
- `DevExpress.Document.Processor` (for PDF/DOCX extraction)

### Startup Registration
- `NpgsqlDataSourceBuilder.UseVector()` + `UseNpgsql(..., o => o.UseVector())`
- `AddChatClient(openAiChatClient)`
- `AddDevExpressAI()`
- Scoped: `EmbeddingService`, `ChunkingService`, `DocumentProcessingService`, `RagService`

## Future Extensions (not in v1)
- Hybrid search (vector + PostgreSQL full-text with RRF fusion)
- Query expansion via LLM
- Chat history persistence
- Source citations in responses with links to original articles/documents
- Chunk metadata boosting (headers, key phrases)
