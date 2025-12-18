using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

/// <summary>
/// A filter consisting of one or more groups, combined using <see cref="Join"/>.
/// </summary>
public sealed record FilterOptions
{
    /// <summary>
    /// How groups are combined.
    /// </summary>
    public BoolJoin Join { get; set; } = BoolJoin.And;
    
    /// <summary>
    /// The groups of criteria.
    /// </summary>
    public FilterGroup[] Groups { get; set; } = [];

    /// <summary>
    /// Gets a <see cref="FilterOptions"/> with a single group containing the specified criteria, combined using AND.
    /// </summary>
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