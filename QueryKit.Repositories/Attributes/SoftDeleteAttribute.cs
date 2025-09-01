using System;

namespace QueryKit.Repositories.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class SoftDeleteAttribute : Attribute
{
    
}