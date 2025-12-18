using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Sorting;

/// <summary>
/// A set of sort criteria applied in sequence (primary, secondary, etc.).
/// </summary>
public sealed record SortOptions()
{
    /// <summary>
    /// Sort criteria to apply. Evaluated in array order.
    /// </summary>
    public SortCriterion[] Criteria { get; set; } = [];

    /// <summary>
    /// Creates a SortOptions instance with a single criterion.
    /// </summary>
    public static SortOptions By(string columnName, SortDirection direction = SortDirection.Ascending)
        => new() { Criteria = new[] { new SortCriterion { ColumnName = columnName, Direction = direction } } };
}