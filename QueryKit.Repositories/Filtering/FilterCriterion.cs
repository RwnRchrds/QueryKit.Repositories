using System;
using QueryKit.Repositories.Enums;

namespace QueryKit.Repositories.Filtering;

/// <summary>
/// A single filter expression against a column.
/// </summary>
public sealed record FilterCriterion
{
    /// <summary>
    /// Column or DTO property name.
    /// </summary>
    public required string ColumnName { get; init; }
    
    /// <summary>
    /// Operator to apply.
    /// </summary>
    public required FilterOperator Operator { get; init; }
    
    /// <summary>
    /// Primary value; meaning depends on <see cref="Operator"/>.
    /// </summary>
    public object Value { get; init; } = null;
    
    /// <summary>
    /// Secondary value for operators that require two values (e.g., <see cref="FilterOperator.Between"/>).
    /// </summary>
    public object Value2 { get; init; } = null;
    
    /// <summary>
    /// Value set for <see cref="FilterOperator.In"/>.
    /// </summary>
    public object[] Values {get; init; } = [];
}