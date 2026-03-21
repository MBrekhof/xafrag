# ASP.NET Core Blazor Components

## Overview

Blazor apps are built using Razor components (informally known as Blazor components or components). A component is a self-contained portion of user interface (UI) with processing logic to enable dynamic behavior. Components can be nested, reused, shared among projects, and used in MVC and Razor Pages apps.

Components render into an in-memory representation of the browser's Document Object Model (DOM) called a render tree, which is used to update the UI in a flexible and efficient way.

## Component Classes

Components are implemented using a combination of C# and HTML markup in Razor component files with the `.razor` file extension.

### Base Classes

`ComponentBase` is the base class for components described by Razor component files. It implements the `IComponent` interface and defines component properties and methods for basic functionality, including processing built-in component lifecycle events.

## Razor Syntax

Components use Razor syntax with two extensively used features: directives and directive attributes.

### Directives

Directives are reserved keywords prefixed with `@`:

```razor
@page "/doctor-who-episodes/{season:int}"
@rendermode InteractiveWebAssembly
@using System.Globalization
@using Microsoft.AspNetCore.Localization
@attribute [Authorize]
@implements IAsyncDisposable
@inject IJSRuntime JS
```

### Directive Attributes

Change the way a component element is compiled or functions:

```razor
<input @bind="episodeId" />
```

## Component Naming

A component's name must start with an uppercase character:

- Supported: `ProductDetail.razor`
- Unsupported: `productDetail.razor`

File paths and file names use Pascal case. Component file paths for routable components match their URLs in kebab case.

## Partial Class Support

Components are generated as C# partial classes and can be authored in two ways:

### Single File Approach

```razor
@page "/counter"

<PageTitle>Counter</PageTitle>
<h1>Counter</h1>
<p role="status">Current count: @currentCount</p>
<button class="btn btn-primary" @onclick="IncrementCount">Click me</button>

@code {
    private int currentCount = 0;
    private void IncrementCount() => currentCount++;
}
```

### Code-Behind Approach

**CounterPartialClass.razor:**

```razor
@page "/counter-partial-class"

<PageTitle>Counter</PageTitle>
<h1>Counter</h1>
<p role="status">Current count: @currentCount</p>
<button class="btn btn-primary" @onclick="IncrementCount">Click me</button>
```

**CounterPartialClass.razor.cs:**

```csharp
namespace BlazorSample.Components.Pages;

public partial class CounterPartialClass
{
    private int currentCount = 0;
    private void IncrementCount() => currentCount++;
}
```

## Component Parameters

Component parameters pass data to components and are defined using public C# properties with the `[Parameter]` attribute.

### Basic Example

```razor
<div class="card w-25" style="margin-bottom:15px">
    <div class="card-header font-weight-bold">@Title</div>
    <div class="card-body">
        <p>@Body.Text</p>
    </div>
</div>

@code {
    [Parameter]
    public string Title { get; set; } = "Set By Child";

    [Parameter]
    public PanelBody Body { get; set; } = new()
    {
        Text = "Card content set by child.",
        Style = "normal"
    };

    [Parameter]
    public int? Count { get; set; }
}
```

### EditorRequired Attribute

```csharp
[Parameter]
[EditorRequired]
public string? Title { get; set; }
```

## Route Parameters

Components can specify route parameters in the route template:

```razor
@page "/route-parameter-1/{text}"

<p>Blazor is @Text!</p>

@code {
    [Parameter]
    public string? Text { get; set; }
}
```

## Child Content (Render Fragments)

Components can set the content of another component using a `RenderFragment` parameter:

```razor
<div class="card w-25">
    <div class="card-header font-weight-bold">Child content</div>
    <div class="card-body">@ChildContent</div>
</div>

@code {
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

Usage:

```razor
<RenderFragmentChild>
    Content of the child component is supplied
    by the parent component.
</RenderFragmentChild>
```

## Component References

Capture references to component instances for issuing commands:

```razor
<ReferenceChild @ref="childComponent" />

@code {
    private ReferenceChild? childComponent;
}
```

**Important:** Component references are only populated after the component is rendered. Don't use component references to mutate child component state — use component parameters instead.

## Nested Components

Components can include other components by declaring them using HTML syntax:

```razor
@page "/heading-example"

<h1>Heading Example</h1>
<Heading />
```

## Base Class Inheritance

Use the `@inherits` directive to specify a base class:

```razor
@page "/blazor-rocks-1"
@inherits BlazorRocksBase1

<h1>Blazor Rocks! Example 1</h1>
<p>@BlazorRocksText</p>
```

## Asynchronous Methods

Always return a `Task` or `ValueTask` for async methods — never `async void`:

```csharp
// Incorrect
public async void MyAsyncMethod() { }

// Correct
public async Task MyAsyncMethod() { }
```

## Raw HTML Rendering

Use `MarkupString` to render raw HTML:

```razor
@((MarkupString)myMarkup)

@code {
    private string myMarkup =
        "<p class=\"text-danger\">This is a <em>markup string</em>.</p>";
}
```

**Warning:** Rendering HTML from untrusted sources is a security risk — always sanitize input.

Source: Microsoft Learn - ASP.NET Core Blazor documentation
