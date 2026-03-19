namespace XafRag.Blazor.Server.Configuration;

public class OpenAiOptions
{
    public const string SectionName = "OpenAI";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string ChatModel { get; set; } = "gpt-4o";
}
