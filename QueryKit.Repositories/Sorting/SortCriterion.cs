using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Sorting;

/// <summary>
/// Represents a single sort order by column.
/// </summary>
public sealed record SortCriterion
{
    /// <summary>
    /// Column or DTO property name.
    /// </summary>
    public required string ColumnName { get; set; }
    
    /// <summary>
    /// Sort direction.
    /// </summary>
    public SortDirection Direction { get; set; } = SortDirection.Ascending;
}