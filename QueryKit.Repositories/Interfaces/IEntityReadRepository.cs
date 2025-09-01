using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;

namespace QueryKit.Repositories.Interfaces;

public interface IEntityReadRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<bool> IsUniqueIncludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default);

    Task<bool> IsUniqueExcludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default);
}