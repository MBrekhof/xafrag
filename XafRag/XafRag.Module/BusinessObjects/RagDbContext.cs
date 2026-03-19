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
