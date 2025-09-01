using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Sorting;

public sealed record SortOptions()
{
    public SortCriterion[] Criteria { get; init; } = [];

    public static SortOptions By(string columnName, SortDirection direction = SortDirection.Ascending)
        => new() { Criteria = new[] { new SortCriterion { ColumnName = columnName, Direction = direction } } };
}