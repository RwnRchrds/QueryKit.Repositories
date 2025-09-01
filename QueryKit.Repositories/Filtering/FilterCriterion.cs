using System;
using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

public sealed record FilterCriterion
{
    public required string ColumnName { get; init; }
    public required FilterOperator Operator { get; init; }
    public object Value { get; init; } = null;
    public object Value2 { get; init; } = null;
    public object[] Values {get; init; } = [];
}