using System;
using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

public sealed record FilterGroup
{
    public BoolJoin Join { get; init; }
    public FilterCriterion[] Criteria { get; init; } = [];
}