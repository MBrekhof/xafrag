# Entity Framework Core - Relationships

## Overview

This document provides an introduction to the representation of relationships in object models and relational databases, including how EF Core maps between the two.

## Relationships in Object Models

A relationship defines how two entities relate to each other. For example, when modeling posts in a blog, each post is related to the blog it is published on, and the blog is related to all the posts published on that blog.

In C#, the blog and post are typically represented by two classes:

```csharp
public class Blog
{
    public string Name { get; set; }
    public virtual Uri SiteUri { get; set; }
}

public class Post
{
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime PublishedOn { get; set; }
    public bool Archived { get; set; }
}
```

To indicate that Blog and Post are related, add a reference from Post to Blog:

```csharp
public class Post
{
    public string Title { get; set; }
    public string Content { get; set; }
    public DateOnly PublishedOn { get; set; }
    public bool Archived { get; set; }

    public Blog Blog { get; set; }
}
```

And a collection of Post objects on each Blog:

```csharp
public class Blog
{
    public string Name { get; set; }
    public virtual Uri SiteUri { get; set; }

    public ICollection<Post> Posts { get; }
}
```

This connection from Blog to Post and, inversely, from Post back to Blog is known as a "relationship" in EF Core. In EF Core, the `Blog.Posts` and `Post.Blog` properties are called "navigations".

**Important:** A single relationship can typically be traversed in either direction. This is one relationship, not two.

## Relationships in Relational Databases

Relational databases represent relationships using foreign keys:

```sql
CREATE TABLE [Posts] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(max) NULL,
    [Content] nvarchar(max) NULL,
    [PublishedOn] datetime2 NOT NULL,
    [Archived] bit NOT NULL,
    [BlogId] int NOT NULL,
    CONSTRAINT [PK_Posts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Posts_Blogs_BlogId] FOREIGN KEY ([BlogId])
        REFERENCES [Blogs] ([Id]) ON DELETE CASCADE);

CREATE TABLE [Blogs] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NULL,
    [SiteUri] nvarchar(max) NULL,
    CONSTRAINT [PK_Blogs] PRIMARY KEY ([Id]));
```

The `BlogId` foreign key column of the Posts table references the `Id` primary key column of the Blogs table. This determines which blog every post is related to.

## Mapping Relationships in EF Core

EF Core relationship mapping involves:

1. Adding a primary key property to each entity type
2. Adding a foreign key property to one entity type
3. Associating the references between entity types with the primary and foreign keys

```csharp
public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; }
    public virtual Uri SiteUri { get; set; }

    public ICollection<Post> Posts { get; }
}

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime PublishedOn { get; set; }
    public bool Archived { get; set; }

    public int BlogId { get; set; }
    public Blog Blog { get; set; }
}
```

This can also be specified explicitly in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Blog>()
        .HasMany(e => e.Posts)
        .WithOne(e => e.Blog)
        .HasForeignKey(e => e.BlogId)
        .HasPrincipalKey(e => e.Id);
}
```

## Types of Relationships

### One-to-Many

A single entity is associated with any number of other entities. This is the most common relationship type (e.g., Blog has many Posts).

### One-to-One

A single entity is associated with another single entity (e.g., Blog has one BlogHeader).

### Many-to-Many

Any number of entities are associated with any number of other entities (e.g., Posts can have many Tags, and Tags can be on many Posts).

## Using Relationships

### Eager Loading

Load related data as part of the LINQ query using `Include`:

```csharp
var blogs = context.Blogs
    .Include(b => b.Posts)
    .ToList();
```

### Lazy Loading

Related data is transparently loaded when the navigation property is accessed. Requires proxy configuration.

### Explicit Loading

Related data is explicitly loaded at a later time:

```csharp
var blog = context.Blogs.Single(b => b.BlogId == 1);
context.Entry(blog).Collection(b => b.Posts).Load();
```

## Cascade Deletes

Related entities can be automatically deleted when `SaveChanges` or `SaveChangesAsync` is called, depending on the delete behavior configured.

## Owned Entity Types

Owned entity types use a special "owning" relationship that implies a stronger connection between two types than normal relationships.

**Important:** The model-building API is the final source of truth for the EF model — it always takes precedence over configuration discovered by convention or specified by mapping attributes.

Source: Microsoft Learn - Entity Framework Core documentation
