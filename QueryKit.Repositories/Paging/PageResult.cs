using System.Collections.Generic;

namespace QueryKit.Repositories.Paging;

public sealed record PageResult<TEntity>
{
    public required IList<TEntity> Items { get; init; }
    public required int TotalItems { get; init; }
};