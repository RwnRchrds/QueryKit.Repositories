using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;

namespace QueryKit.Repositories.Interfaces;

public interface IEntityReadRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// Retrieves an entity by primary key.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a raw SQL query and returns a single entity (or <c>null</c>).
    /// </summary>
    Task<TEntity?> GetAsync(string sql, object? parameters, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a raw SQL query and returns a list of entities.
    /// </summary>
    Task<IList<TEntity>> GetListAsync(string sql, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paged list of entities with optional filtering and sorting.
    /// </summary>
    Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for uniqueness of a column including soft-deleted rows.
    /// </summary>
    Task<bool> IsUniqueIncludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for uniqueness of a column excluding soft-deleted rows.
    /// </summary>
    Task<bool> IsUniqueExcludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a raw SQL query and maps to a single instance of <typeparamref name="T"/>.
    /// </summary>
    Task<T?> GetAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a raw SQL query and maps to a list of <typeparamref name="T"/>.
    /// </summary>
    Task<IList<T>> GetListAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves a paged, filtered, and sorted result for a custom DTO query.
    /// Sort options replace any existing ORDER BY in the base SQL. No PK fallback is injected.
    /// </summary>
    Task<PageResult<T>> GetListPagedAsync<T>(string sql, object? parameters, FilterOptions? filter, 
        SortOptions? sort, PageOptions? paging, CancellationToken cancellationToken = default);

    Task<PageResult<T>> GetListPagedAsync<T>(string dataSql, string countSql, object? parameters,
        PageOptions paging, CancellationToken cancellationToken = default);
}