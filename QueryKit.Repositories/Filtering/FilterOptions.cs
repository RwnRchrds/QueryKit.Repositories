using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

public sealed record FilterOptions
{
    public BoolJoin Join { get; init; } = BoolJoin.And;
    public FilterGroup[] Groups { get; init; } = [];

    public static FilterOptions From(params FilterCriterion[] criteria) =>
        new()
        {
            Join = BoolJoin.And,
            Groups =
            [
                new FilterGroup { Join = BoolJoin.And, Criteria = criteria }
            ]
        };
}