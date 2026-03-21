# XAF Security System - Password Management

## Overview

This document covers password management within XAF's (eXpressAppFramework) security infrastructure, specifically for applications using `AuthenticationStandard` authentication.

## Password Encryption

XAF employs modern cryptographic standards for password storage. The `ApplicationUser` class stores passwords as hash codes using the **RFC 2898 algorithm** via the `Rfc2898DeriveBytes` class. This provides secure, salted password hashing.

**Important Migration Note:** Applications using legacy SHA-512 hashing must update to RFC 2898 before upgrading to version 25.2 or later. This mandatory update enhances security by replacing outdated algorithms.

## Administrator-Generated Passwords

System administrators can leverage the **ResetPassword Action** to initialize user passwords. This capability:

- Requires the user type to implement `IAuthenticationStandardUser` interface
- Operates within Standard Authentication contexts
- Is provided by the `ResetPasswordController` view controller
- Is located in the RecordEdit action container

The action triggers a dialog allowing administrators to generate temporary passwords that users can modify after first login.

**Caveat:** Unsaved Detail View changes are discarded when executing ResetPassword. Enable the `SaveUserObjectOnPasswordChanging` property to prevent data loss.

## Mandatory Password Change on First Logon

Users implementing `IAuthenticationStandardUser` expose the `ChangePasswordOnFirstLogon` property. When enabled for a user account, a password change dialog appears immediately after their initial login.

This feature exclusively functions with Standard Authentication, as Active Directory authentication doesn't expect XAF-managed password modifications.

## End-User Password Modifications

Standard Authentication allows users accessing the **My Details** view to change their passwords via the **ChangeMyPassword Action**. Users can:

- Update their own passwords independently
- Access this feature through the Edit action container
- Use a dedicated dialog interface

Similar to administrator resets, unsaved changes are lost unless `SaveUserObjectOnPasswordChanging` is configured.

## Programmatic Password Access

Passwords cannot be decrypted from stored hash values. For custom password handling, use these approaches:

### Custom Delegate Methods

```csharp
using DevExpress.Persistent.Base;

PasswordCryptographer.VerifyHashedPasswordDelegate = VerifyHashedPassword;
PasswordCryptographer.HashPasswordDelegate = HashPassword;

static bool VerifyHashedPassword(string saltedPassword, string password) {
    // Custom validation logic
    return result;
}

static string HashPassword(string password) {
    // Custom hashing logic
    return hash;
}
```

### Non-XAF Applications with XPO

```csharp
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using DevExpress.Xpo;

var session = new UnitOfWork();
var user = session.Query<ApplicationUser>()
    .FirstOrDefault(u => u.UserName == "John");
var saltedPassword = (string)user?.GetMemberValue("StoredPassword");
bool passwordMatches = PasswordCryptographer
    .VerifyHashedPasswordDelegate(saltedPassword, "test");
```

### Interface Methods

The `IAuthenticationStandardUser` interface provides:
- `ComparePassword(String)` – validates plain password against stored hash
- `SetPassword(String)` – updates user password programmatically

## Password Complexity Validation

XAF supports enforcing complex password requirements. Detailed implementation strategies are documented in the "Validate Password Complexity" help topic, utilizing non-persistent object validation approaches.

Source: DevExpress XAF documentation
