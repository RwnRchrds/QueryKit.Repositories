using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Dapper;
using QueryKit.Dialects;
using QueryKit.Extensions;
using QueryKit.Repositories.Enums;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;
using QueryKit.Sql;

namespace QueryKit.Repositories.Sql;

/// <summary>
/// SQL composition helpers used by repositories. Provides top-level safe WHERE/ORDER injection,
/// dialect-aware paging, and filter/sort builders that map DTO/entity properties to column names.
/// </summary>
public static class QuerySqlBuilder
{
    private static readonly ConcurrentDictionary<(Type, string, string), string?> ColMapCache
        = new ConcurrentDictionary<(Type, string, string), string?>();

    /// <summary>
    /// Injects a <c>WHERE</c> fragment into <paramref name="sql"/>. If a top-level <c>WHERE</c> exists,
    /// the fragment is joined using <c>AND</c>. Otherwise, a new <c>WHERE</c> is inserted before top-level
    /// <c>ORDER BY</c>, <c>GROUP BY</c>, or <c>HAVING</c>.
    /// Safe for CTEs/subqueries (only top-level clauses are modified).
    /// </summary>
    public static string InjectWhere(string sql, string whereClause)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
            return TrimSemicolon(sql);

        var s = TrimSemicolon(sql);
        var hasWhere = IndexOfFirstTopLevel(s, "WHERE") >= 0;

        if (hasWhere)
        {
            var cut = FirstTopLevelIndexOfAny(s, "ORDER BY", "GROUP BY", "HAVING");
            if (cut < 0) return $"{s.TrimEnd()} AND {whereClause}";
            var head = s.Substring(0, cut).TrimEnd();
            var tail = s.Substring(cut).TrimStart();
            return $"{head} AND {whereClause} {tail}";
        }
        else
        {
            var insert = $"WHERE {whereClause}";
            return InsertBeforeFirstTopLevel(s, insert, "ORDER BY", "GROUP BY", "HAVING");
        }
    }

    /// <summary>
    /// Replaces the last top-level <c>ORDER BY</c> with <paramref name="newOrderBy"/>.
    /// If <paramref name="newOrderBy"/> is empty, the existing <c>ORDER BY</c> is removed.
    /// If none exists and <paramref name="newOrderBy"/> is empty, the SQL is returned unchanged.
    /// </summary>
    public static string ReplaceOrder(string sql, string newOrderBy)
    {
        var s = TrimSemicolon(sql);
        var idx = IndexOfLastTopLevel(s, "ORDER BY");
        if (idx < 0)
            return string.IsNullOrWhiteSpace(newOrderBy) ? s : $"{s} ORDER BY {newOrderBy}";
        var head = s.Substring(0, idx).TrimEnd();
        return string.IsNullOrWhiteSpace(newOrderBy) ? head : $"{head} ORDER BY {newOrderBy}";
    }

    /// <summary>
    /// Strips the last top-level <c>ORDER BY</c> clause from <paramref name="sql"/>.
    /// Useful when wrapping a data query into <c>SELECT COUNT(1) FROM (...)</c>.
    /// </summary>
    public static string StripTrailingOrder(string sql)
        => RemoveLastTopLevelTail(sql, "ORDER BY");

    /// <summary>
    /// Appends a dialect-appropriate paging suffix (OFFSET/FETCH or LIMIT/OFFSET) to <paramref name="sql"/>.
    /// </summary>
    /// <param name="sql">Base SQL with any ordering already applied.</param>
    /// <param name="paging">Paging options.</param>
    /// <returns>SQL with paging applied.</returns>
    public static string AppendPaging(string sql, PageOptions paging)
    {
        if (ConnectionExtensions.Config.Dialect == Dialect.SQLServer &&
            IndexOfLastTopLevel(TrimSemicolon(sql), "ORDER BY") < 0)
            throw new ArgumentException("SQL Server requires an ORDER BY when using OFFSET/FETCH.");
        
        var dialect = ConnectionExtensions.Config.Dialect;
        var s = TrimSemicolon(sql);
        var page = paging.PageClamped;
        var size = paging.PageSizeClamped;
        var offset = (page - 1) * size;

        return dialect switch
        {
            Dialect.SQLServer => $"{s} OFFSET {offset} ROWS FETCH NEXT {size} ROWS ONLY",
            Dialect.PostgreSQL => $"{s} LIMIT {size} OFFSET {offset}",
            Dialect.MySQL => $"{s} LIMIT {offset}, {size}",
            _ => $"{s} LIMIT {size} OFFSET {offset}"
        };
    }

    /// <summary>
    /// Builds a SQL WHERE fragment and parameters from <paramref name="filter"/>, mapping
    /// property/alias names via <see cref="MapPropertyToColumn{T}"/>.
    /// Returns an empty WHERE fragment if no criteria are present.
    /// </summary>
    /// <typeparam name="T">Target projection/DTO or entity type being filtered.</typeparam>
    public static (string whereSql, DynamicParameters parameters) BuildWhere<T>(FilterOptions? filter)
    {
        var dialect = ConnectionExtensions.Config.Dialect;
        var dp = new DynamicParameters();

        if (filter is null || filter.Groups.Length == 0)
            return ("", dp);

        var groupSql = new List<string>();
        var pIndex = 0;

        foreach (var group in filter.Groups)
        {
            var parts = new List<string>();
            foreach (var criterion in group.Criteria)
            {
                var conv = new SqlConvention();
                var col = MapPropertyToColumn<T>(criterion.ColumnName);
                if (col is null)
                    throw new ArgumentException(
                        $"Unknown column \'{criterion.ColumnName}\' for type {typeof(T).Name}. Only mapped properties are allowed.");

                string Param(object? v)
                {
                    var name = $"__qk{pIndex++}";
                    dp.Add(name, v);
                    return "@" + name;
                }

                string EscapeUser(string input)
                {
                    return input.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                }

                string LikeContains(object? v)
                {
                    var name = $"__qk{pIndex++}";
                    var s = Convert.ToString(v);
                    dp.Add(name, $"%{EscapeUser(s)}%");
                    return dialect == Dialect.PostgreSQL
                        ? $"{col} ILIKE @{name}"
                        : $"{col} LIKE @{name} ESCAPE '\\'";
                }

                string LikeStartsWith(object? v)
                {
                    var name = $"__qk{pIndex++}";
                    var s = Convert.ToString(v);
                    dp.Add(name, $"{EscapeUser(s)}%");
                    return dialect == Dialect.PostgreSQL
                        ? $"{col} ILIKE @{name}"
                        : $"{col} LIKE @{name} ESCAPE '\\'";
                }

                string LikeEndsWith(object? v)
                {
                    var name = $"__qk{pIndex++}";
                    var s = Convert.ToString(v);
                    dp.Add(name, $"%{EscapeUser(s)}");
                    return dialect == Dialect.PostgreSQL
                        ? $"{col} ILIKE @{name}"
                        : $"{col} LIKE @{name} ESCAPE '\\'";
                }

                parts.Add(criterion.Operator switch
                {
                    FilterOperator.Equals => $"{col} = {Param(criterion.Value)}",
                    FilterOperator.NotEquals => $"{col} <> {Param(criterion.Value)}",
                    FilterOperator.Contains      => LikeContains(criterion.Value),
                    FilterOperator.NotContains   => $"NOT ({LikeContains(criterion.Value)})",
                    FilterOperator.StartsWith    => LikeStartsWith(criterion.Value),
                    FilterOperator.EndsWith      => LikeEndsWith(criterion.Value),
                    FilterOperator.LessThan => $"{col} < {Param(criterion.Value)}",
                    FilterOperator.LessThanOrEqual => $"{col} <= {Param(criterion.Value)}",
                    FilterOperator.GreaterThan => $"{col} > {Param(criterion.Value)}",
                    FilterOperator.GreaterThanOrEqual => $"{col} >= {Param(criterion.Value)}",
                    FilterOperator.In => BuildInClause(col, criterion, dp, ref pIndex),
                    FilterOperator.Between => $"{col} BETWEEN {Param(criterion.Value)} AND {Param(criterion.Value2)}",
                    FilterOperator.IsNull => $"{col} IS NULL",
                    FilterOperator.IsNotNull => $"{col} IS NOT NULL",
                    _ => $"{col} = {Param(criterion.Value)}"
                });
            }

            if (parts.Count > 0)
            {
                var join = group.Join == BoolJoin.Or ? " or " : " and ";
                groupSql.Add(string.Join(join, parts));
            }
        }

        if (groupSql.Count == 0)
            return ("", dp);

        var outerJoin = filter.Join == BoolJoin.Or ? " or " : " and ";
        return (string.Join(outerJoin, groupSql), dp);
    }

    /// <summary>
    /// Builds a SQL ORDER BY fragment from <paramref name="sort"/>, mapping property/alias names via
    /// <see cref="MapPropertyToColumn{T}"/>. Returns an empty string if no criteria are present.
    /// </summary>
    /// <typeparam name="T">Target projection/DTO or entity type being sorted.</typeparam>
    public static string BuildOrderBy<T>(SortOptions? sort)
    {
        if (sort?.Criteria is null || sort.Criteria.Length == 0)
            return "";

        var parts = new List<string>();
        foreach (var c in sort.Criteria)
        {
            var col = MapPropertyToColumn<T>(c.ColumnName);
            if (col is null)
                throw new ArgumentException(
                    $"Unknown sortable column \'{c.ColumnName}\' for type {typeof(T).Name}. Only mapped properties are allowed.");
            var dir = c.Direction == SortDirection.Descending ? "desc" : "asc";
            parts.Add($"{col} {dir}");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Maps a property (or DTO alias) name on <typeparamref name="T"/> to a database column.
    /// </summary>
    /// <typeparam name="T">Type declaring the property/alias.</typeparam>
    /// <param name="candidate">Property or alias name.</param>
    /// <returns>Resolved column name or <c>null</c> if the property is unknown.</returns>
    public static string? MapPropertyToColumn<T>(string candidate)
    {
        var key = (typeof(T), candidate.ToLowerInvariant(), ConnectionExtensions.Config.Dialect.ToString());
        return ColMapCache.GetOrAdd(key, k =>
        {
            var (t, name, _) = k;
            var pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (pi == null) return null;
            var conv = new SqlConvention();
            return conv.Encapsulate(conv.GetColumnName(pi));
        });
    }
    
    /// <summary>
    /// Merges two parameter objects (including <see cref="DynamicParameters"/>),
    /// returning a new <see cref="DynamicParameters"/> suitable for Dapper execution.
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static DynamicParameters MergeParams(object? a, object? b)
    {
        var merged = new DynamicParameters();
        if (a != null) merged.AddDynamicParams(a);
        if (b != null) merged.AddDynamicParams(b);
        return merged;
    }

    private static string RemoveLastTopLevelTail(string sql, string token)
    {
        var idx = IndexOfLastTopLevel(sql, token);
        return idx < 0 ? TrimSemicolon(sql) : sql.Substring(0, idx).TrimEnd();
    }

    private static string InsertBeforeFirstTopLevel(string sql, string insertText, params string[] tokens)
    {
        var min = -1;
        foreach (var t in tokens)
        {
            var i = IndexOfFirstTopLevel(sql, t);
            if (i >= 0 && (min < 0 || i < min)) min = i;
        }

        if (min < 0) return TrimSemicolon(sql) + " " + insertText;
        var head = sql.Substring(0, min).TrimEnd();
        var tail = sql.Substring(min).TrimStart();
        return head + " " + insertText + " " + tail;
    }

    private static int IndexOfFirstTopLevel(string sql, string token)
        => FindTopLevel(sql, token, first: true);

    private static int IndexOfLastTopLevel(string sql, string token)
        => FindTopLevel(sql, token, first: false);

    private static int FindTopLevel(string sql, string token, bool first)
    {
        int depth = 0;
        bool inSq = false, inDq = false, inLine = false, inBlock = false;

        int matchIdx = -1;
        for (int i = 0; i < sql.Length; i++)
        {
            char c = sql[i];
            if (inLine)
            {
                if (c == '\n') inLine = false;
                continue;
            }

            if (inBlock)
            {
                if (c == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                {
                    inBlock = false;
                    i++;
                }

                continue;
            }

            if (inSq)
            {
                if (c == '\'' && !(i + 1 < sql.Length && sql[i + 1] == '\'')) inSq = false;
                continue;
            }

            if (inDq)
            {
                if (c == '"') inDq = false;
                continue;
            }

            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                inLine = true;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                inBlock = true;
                i++;
                continue;
            }

            if (c == '\'')
            {
                inSq = true;
                continue;
            }

            if (c == '"')
            {
                inDq = true;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                if (depth > 0) depth--;
                continue;
            }

            if (depth == 0 && i + token.Length <= sql.Length &&
                sql.Substring(i, token.Length).Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                if (first) return i;
                matchIdx = i;
            }
        }

        return matchIdx;
    }

    private static int FirstTopLevelIndexOfAny(string sql, params string[] tokens)
    {
        int min = -1;
        foreach (var t in tokens)
        {
            var i = IndexOfFirstTopLevel(sql, t);
            if (i >= 0 && (min < 0 || i < min)) min = i;
        }

        return min;
    }

    private static string TrimSemicolon(string sql) => sql.TrimEnd().TrimEnd(';');

    private static string BuildInClause(string col, FilterCriterion criterion, DynamicParameters dp, ref int pIndex)
    {
        IEnumerable<object> vals = criterion.Values;

        var any = false;
        var names = new List<string>();
        foreach (var v in vals)
        {
            any = true;
            var pn = $"__qk{pIndex++}";
            dp.Add(pn, v);
            names.Add("@" + pn);
        }

        if (!any)
            return "1=0";

        return $"{col} IN ({string.Join(",", names)})";
    }
}