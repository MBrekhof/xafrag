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
