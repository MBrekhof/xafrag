# Entity Framework Core - Querying Data

## Overview

Entity Framework Core uses Language-Integrated Query (LINQ) to query data from the database. LINQ allows you to use C# (or your .NET language of choice) to write strongly typed queries. It uses your derived context and entity classes to reference database objects. EF Core passes a representation of the LINQ query to the database provider. Database providers in turn translate it to database-specific query language (for example, SQL for a relational database). Queries are always executed against the database even if the entities returned in the result already exist in the context.

## Loading All Data

```csharp
using (var context = new BloggingContext())
{
    var blogs = await context.Blogs.ToListAsync();
}
```

## Loading a Single Entity

```csharp
using (var context = new BloggingContext())
{
    var blog = await context.Blogs
        .SingleAsync(b => b.BlogId == 1);
}
```

## Filtering

```csharp
using (var context = new BloggingContext())
{
    var blogs = await context.Blogs
        .Where(b => b.Url.Contains("dotnet"))
        .ToListAsync();
}
```

## How Queries Work

EF Core translates LINQ queries into SQL (or the appropriate query language for the target database). The process works as follows:

1. You write a LINQ query in C#
2. EF Core's query pipeline translates the LINQ expression tree into a database-specific query
3. The query is sent to the database for execution
4. Results are materialized into .NET objects

## Related Data Loading

EF Core provides several strategies for loading related data:

### Eager Loading

Load related data as part of the initial query using `Include`:

```csharp
var blogs = context.Blogs
    .Include(b => b.Posts)
    .ToList();
```

### Lazy Loading

Related data is transparently loaded from the database when the navigation property is accessed. Requires:
- Installing `Microsoft.EntityFrameworkCore.Proxies` package
- Calling `UseLazyLoadingProxies()` in configuration
- Making navigation properties `virtual`

### Explicit Loading

Related data is explicitly loaded from the database at a later time:

```csharp
var blog = context.Blogs.Single(b => b.BlogId == 1);
context.Entry(blog).Collection(b => b.Posts).Load();
```

## Tracking vs No-Tracking Queries

By default, queries that return entity types are tracked. This means changes to entity instances are detected by `ChangeTracker`. Use `AsNoTracking()` for read-only queries to improve performance:

```csharp
var blogs = context.Blogs
    .AsNoTracking()
    .ToList();
```

## Raw SQL Queries

When LINQ isn't sufficient, you can use raw SQL:

```csharp
var blogs = context.Blogs
    .FromSqlRaw("SELECT * FROM Blogs WHERE Url LIKE '%dotnet%'")
    .ToList();
```

## Global Query Filters

EF Core allows defining global query filters on entity types in the model. These filters are automatically applied to all queries:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>()
        .HasQueryFilter(b => !b.IsDeleted);
}
```

This is useful for implementing soft-delete patterns or multi-tenancy.

Source: Microsoft Learn - Entity Framework Core documentation
