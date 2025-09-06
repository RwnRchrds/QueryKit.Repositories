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
    /// Retrieves a paged list of entities with optional filtering and sorting.
    /// </summary>
    Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a list of entities with optional filtering and sorting.
    /// </summary>
    Task<IList<TEntity>> GetListAsync(FilterOptions? filter = null, SortOptions? sort = null,
        bool includeDeleted = false, CancellationToken cancellationToken = default);

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
}