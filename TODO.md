# TODO

## P2: Medium

#### RAG-001: Cascade-delete KnowledgeChunks when a Document is deleted

Deleting a Document in XAF leaves its KnowledgeChunks orphaned: chunks live in a separate
`RagDbContext` with no FK to the XAF `Document` entity, so EF Core cascade delete never fires.
Orphans waste storage and pollute RAG search results. A manual cleanup on 2026-03-21 removed
43 orphaned chunks from 5 deleted documents.

Plan: ViewController (or `ObjectSpace.ObjectDeleting` handler) on Document deletion that deletes
matching `knowledge_chunks` rows by document id.

## P3: Low

#### RAG-002: Web search integration for RAG chat

Knowledge base only covers uploaded documents; web search would let the chat answer beyond it.
Two candidate approaches (decision deferred to implementation time):

- a) Standalone search API (Tavily or Bing): add a `WebSearchService`, merge results into the RAG
  prompt alongside vector-search chunks. Easiest fit with the existing `IChatClient` pipeline.
- b) OpenAI Responses API with `web_search` tool: OpenAI searches internally, but bypasses
  `Microsoft.Extensions.AI`'s `IChatClient` (Responses API not supported there).
