using System;

namespace QueryKit.Repositories.Exceptions;

/// <summary>
/// Thrown when an optimistic-concurrency update matches no rows, meaning the entity was changed or
/// deleted by someone else since the expected version was read.
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// Creates a new instance of <see cref="ConcurrencyException"/>.
    /// </summary>
    public ConcurrencyException(string? message) : base(message)
    {
    }
}
