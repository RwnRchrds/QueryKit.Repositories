using System;
using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

/// <summary>
/// A collection of <see cref="FilterCriterion"/> joined by <see cref="Join"/>.
/// </summary>
public sealed record FilterGroup
{
    /// <summary>
    /// How criteria inside this group are combined.
    /// </summary>
    public BoolJoin Join { get; set; }
    
    /// <summary>
    /// The criteria items in this group.
    /// </summary>
    public FilterCriterion[] Criteria { get; set; } = [];
}