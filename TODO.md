# TODO

## P3: Low

#### RAG-002: Web search integration for RAG chat

Knowledge base only covers uploaded documents; web search would let the chat answer beyond it.
Two candidate approaches (decision deferred to implementation time):

- a) Standalone search API (Tavily or Bing): add a `WebSearchService`, merge results into the RAG
  prompt alongside vector-search chunks. Easiest fit with the existing `IChatClient` pipeline.
- b) OpenAI Responses API with `web_search` tool: OpenAI searches internally, but bypasses
  `Microsoft.Extensions.AI`'s `IChatClient` (Responses API not supported there).
