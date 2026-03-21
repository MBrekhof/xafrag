# Building a .NET AI RAG App with Vector Search

## Overview

In this quickstart, you create a .NET console app to perform semantic search on a vector store to find relevant results for the user's query. You learn how to generate embeddings for user prompts and use those embeddings to query the vector data store.

## What are Vector Stores?

Vector stores, or vector databases, are essential for tasks like semantic search, retrieval augmented generation (RAG), and other scenarios that require grounding generative AI responses. While relational databases and document databases are optimized for structured and semi-structured data, vector databases are built to efficiently store, index, and manage data represented as embedding vectors. As a result, the indexing and search algorithms used by vector databases are optimized to efficiently retrieve data that can be used downstream in your applications.

## Key Libraries

### Microsoft.Extensions.AI

Allows you to write code using AI abstractions rather than a specific SDK. AI abstractions help create loosely coupled code that allows you to change the underlying AI model with minimal app changes.

### Microsoft.Extensions.VectorData.Abstractions

A .NET library that provides a unified layer of abstractions for interacting with vector stores. It provides:
- Perform create-read-update-delete (CRUD) operations on vector stores.
- Use vector and text search on vector stores.

## Prerequisites

- .NET 8.0 SDK or higher
- An API key from OpenAI

## Required Packages

```bash
dotnet add package Microsoft.Extensions.AI.OpenAI
dotnet add package Microsoft.Extensions.VectorData.Abstractions
dotnet add package Microsoft.SemanticKernel.Connectors.InMemory --prerelease
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.UserSecrets
dotnet add package System.Linq.AsyncEnumerable
```

## Data Model

Define a data model with vector store attributes:

```csharp
using Microsoft.Extensions.VectorData;

namespace VectorDataAI;

internal class CloudService
{
    [VectorStoreKey]
    public int Key { get; set; }

    [VectorStoreData]
    public string Name { get; set; }

    [VectorStoreData]
    public string Description { get; set; }

    [VectorStoreVector(
        Dimensions: 384,
        DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
```

The `Microsoft.Extensions.VectorData` attributes, such as `VectorStoreKeyAttribute`, influence how each property is handled when used in a vector store. The `Vector` property stores a generated embedding that represents the semantic meaning of the `Description` value for vector searches.

## Sample Data

```csharp
List<CloudService> cloudServices =
[
    new() {
        Key = 0,
        Name = "Azure App Service",
        Description = "Host .NET, Java, Node.js, and Python web applications and APIs in a fully managed Azure service."
    },
    new() {
        Key = 1,
        Name = "Azure Service Bus",
        Description = "A fully managed enterprise message broker supporting both point to point and publish-subscribe integrations."
    },
    new() {
        Key = 2,
        Name = "Azure Blob Storage",
        Description = "Azure Blob Storage allows your applications to store and retrieve files in the cloud."
    },
    new() {
        Key = 3,
        Name = "Microsoft Entra ID",
        Description = "Manage user identities and control access to your apps, data, and resources."
    },
    new() {
        Key = 4,
        Name = "Azure Key Vault",
        Description = "Store and access application secrets like connection strings and API keys in an encrypted vault."
    },
    new() {
        Key = 5,
        Name = "Azure AI Search",
        Description = "Information retrieval at scale for traditional and conversational search applications."
    }
];
```

## Create the Embedding Generator

```csharp
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
string model = config["ModelName"];
string key = config["OpenAIKey"];

IEmbeddingGenerator<string, Embedding<float>> generator =
    new OpenAIClient(new ApiKeyCredential(key))
        .GetEmbeddingClient(model: model)
        .AsIEmbeddingGenerator();
```

## Create and Populate the Vector Store

```csharp
var vectorStore = new InMemoryVectorStore();
VectorStoreCollection<int, CloudService> cloudServicesStore =
    vectorStore.GetCollection<int, CloudService>("cloudServices");
await cloudServicesStore.EnsureCollectionExistsAsync();

foreach (CloudService service in cloudServices)
{
    service.Vector = await generator.GenerateVectorAsync(service.Description);
    await cloudServicesStore.UpsertAsync(service);
}
```

The embeddings are numerical representations of the semantic meaning for each data record, which makes them compatible with vector search features.

## Perform Vector Search

```csharp
string query = "Which Azure service should I use to store my Word documents?";
ReadOnlyMemory<float> queryEmbedding = await generator.GenerateVectorAsync(query);

IAsyncEnumerable<VectorSearchResult<CloudService>> results =
    cloudServicesStore.SearchAsync(queryEmbedding, top: 1);

await foreach (VectorSearchResult<CloudService> result in results)
{
    Console.WriteLine($"Name: {result.Record.Name}");
    Console.WriteLine($"Description: {result.Record.Description}");
    Console.WriteLine($"Vector match score: {result.Score}");
}
```

## How RAG Works

Retrieval Augmented Generation (RAG) is a pattern that combines:

1. **Retrieval**: Finding relevant documents or data chunks using vector similarity search
2. **Augmentation**: Including the retrieved context in the prompt sent to the LLM
3. **Generation**: The LLM generates a response grounded in the retrieved context

This approach helps ensure that AI responses are based on actual data rather than potentially outdated training data, reducing hallucinations and improving accuracy.

## Key Concepts

- **Embeddings**: Numerical vector representations of text that capture semantic meaning
- **Vector Similarity**: Measuring how close two vectors are (commonly using cosine similarity)
- **Chunking**: Breaking large documents into smaller pieces for more precise retrieval
- **Top-K Search**: Retrieving the K most similar results from the vector store

Source: Microsoft Learn - .NET AI documentation
