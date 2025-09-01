namespace QueryKit.Repositories.Enums;

/// <summary>
/// Supported comparison operators for filter criteria.
/// </summary>
public enum FilterOperator
{
    /// <summary>Equal to.</summary>
    Equals,
    /// <summary>Not equal to.</summary>
    NotEquals,
    /// <summary>SQL LIKE '%value%'.</summary>
    Contains,
    /// <summary>SQL NOT LIKE '%value%'.</summary>
    NotContains,
    /// <summary>SQL LIKE 'value%'.</summary>
    StartsWith,
    /// <summary>SQL LIKE '%value'.</summary>
    EndsWith,
    /// <summary>Less than.</summary>
    LessThan,
    /// <summary>Less than or equal.</summary>
    LessThanOrEqual,
    /// <summary>Greater than.</summary>
    GreaterThan,
    /// <summary>Greater than or equal.</summary>
    GreaterThanOrEqual,
    /// <summary>Value is in a set.</summary>
    In,
    /// <summary>Between two values (inclusive).</summary>
    Between,
    /// <summary>Column is null.</summary>
    IsNull,
    /// <summary>Column is not null.</summary>
    IsNotNull
}