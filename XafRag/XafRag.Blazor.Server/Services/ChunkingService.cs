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

            if (paragraphTokens > _options.ChunkTokenLimit)
            {
                if (currentChunk.Count > 0)
                {
                    chunks.Add(CreateChunk(currentChunk, currentTokens, chunkIndex++));
                    currentChunk = GetOverlapParagraphs(currentChunk, _options.ChunkOverlap);
                    currentTokens = EstimateTokens(string.Join("\n\n", currentChunk));
                }

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

    public static int EstimateTokens(string text) => text.Length / 4;
}
