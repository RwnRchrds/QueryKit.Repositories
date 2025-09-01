using System;

namespace QueryKit.Repositories.Attributes;

/// <summary>
/// Identifies a boolean property as the soft-delete column for an entity.
/// When present, delete operations will set this property to <c>true</c> instead of removing the row.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SoftDeleteAttribute : Attribute
{
    
}