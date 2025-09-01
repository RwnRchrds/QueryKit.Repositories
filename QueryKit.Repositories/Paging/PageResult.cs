using System.Collections.Generic;

namespace QueryKit.Repositories.Paging;

/// <summary>
/// Represents a paged result with items and total count.
/// </summary>
public sealed record PageResult<TEntity>
{
    /// <summary>
    /// Items for the current page.
    /// </summary>
    public required IList<TEntity> Items { get; init; }
    
    /// <summary>
    /// Total number of items available across all pages (without paging applied).
    /// </summary>
    public required int TotalItems { get; init; }
};