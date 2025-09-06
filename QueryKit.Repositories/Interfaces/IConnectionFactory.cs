using System.Data;

namespace QueryKit.Repositories.Interfaces;

/// <summary>
/// A factory interface for creating database connections.
/// </summary>
public interface IConnectionFactory
{
    /// <summary>
    /// Creates and returns a new closed database connection.
    /// </summary>
    IDbConnection Create();
}