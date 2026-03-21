# XAF CRUD Operations - Create, Read, Update, and Delete Data

## Overview

XAF (eXpressApp Framework) enables developers to implement business logic in two primary locations:

### In Controllers
Developers can declare and customize Actions and handle Controller events. Once an Object Space instance is obtained, it facilitates data manipulation operations.

### In Model (Business Classes)
Logic can be embedded in property accessors, implemented through lifecycle methods triggered during object creation/loading/saving/deletion, and via action method declarations.

## Core CRUD Tasks

### 1. Create a New Object

To create a new object in XAF, you use the Object Space's `CreateObject<T>()` method:

```csharp
using DevExpress.ExpressApp;

// In a ViewController
IObjectSpace objectSpace = View.ObjectSpace;
var newContact = objectSpace.CreateObject<Contact>();
newContact.FirstName = "John";
newContact.LastName = "Doe";
objectSpace.CommitChanges();
```

### 2. Load Objects

Use the Object Space to query and load objects from the database:

```csharp
// Load a single object by criteria
var contact = objectSpace.FindObject<Contact>(
    CriteriaOperator.Parse("LastName = ?", "Doe"));

// Load a collection of objects
var contacts = objectSpace.GetObjects<Contact>(
    CriteriaOperator.Parse("Department = ?", "Sales"));

// Load all objects of a type
var allContacts = objectSpace.GetObjects<Contact>();
```

### 3. Save Objects to Database

After modifying objects, commit changes through the Object Space:

```csharp
contact.Email = "john.doe@example.com";
objectSpace.CommitChanges();
```

### 4. Delete Objects from Database

```csharp
objectSpace.Delete(contact);
objectSpace.CommitChanges();
```

### 5. Evaluate Scalar Values and Fetch a Portion of Data

XAF supports aggregation and pagination for efficient data retrieval without loading entire datasets into memory.

### 6. Execute Business Logic When a Property is Changed

XAF provides mechanisms to react to property changes for validation, calculated fields, and cascading updates.

### 7. Refresh Objects and Rollback Changes

```csharp
// Refresh objects from the database
objectSpace.Refresh();

// Rollback uncommitted changes
objectSpace.Rollback();
```

## Implementation with XPO

When using XPO (eXpress Persistent Objects), XAF creates an `XPObjectSpace` instance that internally utilizes a `UnitOfWork`. Developers access this through the `XPObjectSpace.Session` property to perform CRUD operations. The framework automatically passes `UnitOfWork` instances to business class constructors.

## Implementation with EF Core

For EF Core implementations, business classes should implement the `IObjectSpaceLink` interface. XAF automatically assigns an Object Space instance to the `IObjectSpaceLink.ObjectSpace` property, enabling consistent data manipulation APIs across both ORM frameworks.

```csharp
using DevExpress.ExpressApp;

public class MyEntity : IObjectSpaceLink {
    // XAF automatically sets this
    IObjectSpace IObjectSpaceLink.ObjectSpace { get; set; }

    public virtual int Id { get; set; }
    public virtual string Name { get; set; }
}
```

## Object Space

The Object Space is XAF's abstraction over the data access layer. It provides a unified API regardless of whether you use XPO or EF Core as your ORM:

- **IObjectSpace** - The main interface for data manipulation
- **XPObjectSpace** - XPO-specific implementation
- **EFCoreObjectSpace** - EF Core-specific implementation

Key methods include:
- `CreateObject<T>()` - Creates a new persistent object
- `GetObjects<T>()` - Retrieves objects matching criteria
- `FindObject<T>()` - Finds a single object
- `Delete()` - Marks an object for deletion
- `CommitChanges()` - Persists all changes to the database
- `Rollback()` - Discards uncommitted changes

Source: DevExpress XAF documentation
