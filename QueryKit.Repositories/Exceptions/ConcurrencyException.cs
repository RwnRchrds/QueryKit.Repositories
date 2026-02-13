using System;

namespace QueryKit.Repositories.Exceptions;

public class ConcurrencyException : Exception
{
    public ConcurrencyException(string? message) : base(message)
    {
    }
}