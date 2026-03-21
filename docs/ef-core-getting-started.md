# Getting Started with Entity Framework Core

In this tutorial, you create a .NET console app that performs data access against a SQLite database using Entity Framework Core.

## Prerequisites

- .NET SDK (8.0 or higher)
- Visual Studio 2022 version 17.4 or later with the .NET desktop development workload

## Create a new project

Using the .NET CLI:

```bash
dotnet new console -o EFGetStarted
cd EFGetStarted
```

## Install Entity Framework Core

To install EF Core, you install the package for the EF Core database provider(s) you want to target. This tutorial uses SQLite because it runs on all platforms that .NET supports.

```bash
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

## Create the model

Define a context class and entity classes that make up the model. Create a file called `Model.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;

public class BloggingContext : DbContext
{
    public DbSet<Blog> Blogs { get; set; }
    public DbSet<Post> Posts { get; set; }

    public string DbPath { get; }

    public BloggingContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "blogging.db");
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}

public class Blog
{
    public int BlogId { get; set; }
    public string Url { get; set; }

    public List<Post> Posts { get; } = new();
}

public class Post
{
    public int PostId { get; set; }
    public string Title { get; set; }
    public string Content { get; set; }

    public int BlogId { get; set; }
    public Blog Blog { get; set; }
}
```

EF Core can also reverse engineer a model from an existing database.

**Tip:** Connection strings should not be stored in the code for production applications. You may also want to split each C# class into its own file.

## Create the database

The following steps use migrations to create a database.

```bash
dotnet tool install --global dotnet-ef
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet ef migrations add InitialCreate
dotnet ef database update
```

The `migrations` command scaffolds a migration to create the initial set of tables for the model. The `database update` command creates the database and applies the new migration to it.

## Create, Read, Update & Delete

Open `Program.cs` and replace the contents with the following code:

```csharp
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

using var db = new BloggingContext();

Console.WriteLine($"Database path: {db.DbPath}.");

// Create
Console.WriteLine("Inserting a new blog");
db.Add(new Blog { Url = "http://blogs.msdn.com/adonet" });
await db.SaveChangesAsync();

// Read
Console.WriteLine("Querying for a blog");
var blog = await db.Blogs
    .OrderBy(b => b.BlogId)
    .FirstAsync();

// Update
Console.WriteLine("Updating the blog and adding a post");
blog.Url = "https://devblogs.microsoft.com/dotnet";
blog.Posts.Add(
    new Post { Title = "Hello World", Content = "I wrote an app using EF Core!" });
await db.SaveChangesAsync();

// Delete
Console.WriteLine("Delete the blog");
db.Remove(blog);
await db.SaveChangesAsync();
```

## Run the app

```bash
dotnet run
```

Source: Microsoft Learn - Entity Framework Core documentation
