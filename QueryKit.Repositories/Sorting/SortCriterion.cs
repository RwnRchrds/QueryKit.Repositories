using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Sorting;

public sealed record SortCriterion
{
    public required string ColumnName { get; init; }
    public SortDirection Direction { get; init; } = SortDirection.Ascending;
}