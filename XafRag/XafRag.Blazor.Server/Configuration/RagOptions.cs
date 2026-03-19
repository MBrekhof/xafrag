namespace XafRag.Blazor.Server.Configuration;

public class RagOptions
{
    public const string SectionName = "Rag";
    public int ChunkTokenLimit { get; set; } = 500;
    public int ChunkOverlap { get; set; } = 100;
    public int MaxResults { get; set; } = 5;
    public double DistanceThreshold { get; set; } = 1.0;
}
