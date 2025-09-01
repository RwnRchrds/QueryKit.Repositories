using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using QueryKit.Attributes;
using QueryKit.Dialects;
using QueryKit.Extensions;
using QueryKit.Repositories.Enums;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;

namespace QueryKit.Repositories.Sql;

public static class QuerySqlBuilder
{
    private static readonly Regex RxOrderBy = new(@"\border\s+by\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxGroupBy = new(@"\bgroup\s+by\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxHaving = new(@"\bhaving\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxWhere = new(@"\bwhere\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string InjectWhere(string sql, string where)
    {
        if (string.IsNullOrWhiteSpace(where))
        {
            return TrimSemicolon(sql);
        }

        var cutIndex = FirstIndex(sql, RxOrderBy, RxGroupBy, RxHaving);

        if (cutIndex < 0)
        {
            return RxWhere.IsMatch(sql) ? $"{TrimSemicolon(sql)} AND {where}" : $"{TrimSemicolon(sql)} WHERE {where}";
        }
        
        var head = sql[..cutIndex];
        var tail = sql[cutIndex..];
        var hasWhere = RxWhere.IsMatch(head);

        return (hasWhere ? $"{head} AND {where} " : $"{head} {where} ") + tail;
    }
    
    public static string InjectOrMergeOrder(string sql, string newOrderBy, string pkFallback)
    {
        var s = TrimSemicolon(sql);
        var hasOrder = RxOrderBy.IsMatch(s);

        if (!hasOrder)
        {
            var order = string.IsNullOrWhiteSpace(newOrderBy) ? $"{pkFallback} ASC" : newOrderBy;
            return $"{s} ORDER BY {order}";
        }

        return ReplaceOrder(s, string.IsNullOrWhiteSpace(newOrderBy) ? $"{pkFallback} ASC" : newOrderBy);
    }
    
    public static string StripTrailingOrder(string sql)
    {
        var s = TrimSemicolon(sql);
        var matches = RxOrderBy.Matches(s);
        if (matches.Count == 0) return s;
        var last = matches[^1];
        return s[..last.Index].TrimEnd();
    }
    
    public static string AppendPaging(string sql, PageOptions paging)
    {
        var dialect = ConnectionExtensions.Config.Dialect;
        var s = TrimSemicolon(sql);
        var page = paging.PageClamped;
        var size = paging.PageSizeClamped;
        var offset = (page - 1) * size;

        return dialect switch
        {
            Dialect.SQLServer   => $"{s} OFFSET {offset} ROWS FETCH NEXT {size} ROWS ONLY",
            Dialect.PostgreSQL  => $"{s} LIMIT {size} OFFSET {offset}",
            Dialect.MySQL       => $"{s} LIMIT {offset}, {size}",
            Dialect.SQLite      => $"{s} LIMIT {size} OFFSET {offset}",
            _                      => $"{s} LIMIT {size} OFFSET {offset}"
        };
    }
    
    private static string AppendOrder(string sql, string newOrder)
    {
        if (string.IsNullOrWhiteSpace(newOrder)) return sql;
        var matches = RxOrderBy.Matches(sql);
        if (matches.Count == 0) return $"{sql} ORDER BY {newOrder}";
        var last = matches[^1];
        var existing = sql[(last.Index + last.Length)..].Trim().TrimEnd(';');
        var head = sql[..last.Index].TrimEnd();
        var merged = string.IsNullOrWhiteSpace(existing) ? newOrder : $"{existing}, {newOrder}";
        return $"{head} ORDER BY {merged}";
    }
    
    public static (string whereSql, DynamicParameters parameters) BuildWhere<TEntity>(FilterOptions? filter)
    {
        var dp = new DynamicParameters();

        if (filter is null || filter.Groups.Length == 0)
        {
            return ("", dp);
        }

        var groupSql = new List<string>();

        var pIndex = 0;

        foreach (var group in filter.Groups)
        {
            var parts = new List<string>();
            foreach (var criterion in group.Criteria)
            {
                var col = MapPropertyToColumn<TEntity>(criterion.ColumnName);
                
                if (col is null)
                {
                    continue;
                }

                string Param(object? v)
                {
                    var name = $"p{pIndex++}";
                    dp.Add(name, v);
                    return "@" + name;
                }

                string Like(string pattern)
                {
                    var name = $"p{pIndex++}";
                    dp.Add(name, pattern.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_"));
                    return $"LOWER({col}) LIKE LOWER(@{name}) ESCAPE '\\'";
                }
                
                parts.Add(criterion.Operator switch
                {
                    FilterOperator.Equals              => $"{col} = {Param(criterion.Value)}",
                    FilterOperator.NotEquals           => $"{col} <> {Param(criterion.Value)}",
                    FilterOperator.Contains            => Like($"%{criterion.Value}%"),
                    FilterOperator.NotContains         => $"NOT ({Like($"%{criterion.Value}%")})",
                    FilterOperator.StartsWith          => Like($"{criterion.Value}%"),
                    FilterOperator.EndsWith            => Like($"%{criterion.Value}"),
                    FilterOperator.LessThan            => $"{col} < {Param(criterion.Value)}",
                    FilterOperator.LessThanOrEqual     => $"{col} <= {Param(criterion.Value)}",
                    FilterOperator.GreaterThan         => $"{col} > {Param(criterion.Value)}",
                    FilterOperator.GreaterThanOrEqual  => $"{col} >= {Param(criterion.Value)}",
                    FilterOperator.In                  => $"{col} IN {Param(criterion.Values)}",
                    FilterOperator.Between             => $"{col} BETWEEN {Param(criterion.Value)} AND {Param(criterion.Value2)}",
                    FilterOperator.IsNull              => $"{col} IS NULL",
                    FilterOperator.IsNotNull           => $"{col} IS NOT NULL",
                    _                                  => $"{col} = {Param(criterion.Value)}"
                });
            }

            if (parts.Count > 0)
            {
                var join = group.Join == BoolJoin.Or ? " or " : " and ";
                groupSql.Add($"{string.Join(join, parts)}");
            }
        }

        if (groupSql.Count == 0)
        {
            return ("", dp);
        }
        
        var outerJoin = filter.Join == BoolJoin.Or ? " or " : " and ";
        return (string.Join(outerJoin, groupSql), dp);
    }
    
    public static string BuildOrderBy<TEntity>(SortOptions? sort)
    {
        if (sort is null || sort.Criteria.Length == 0)
        {
            return "";
        }

        var parts = new List<string>();

        foreach (var criterion in sort.Criteria)
        {
            var col = MapPropertyToColumn<TEntity>(criterion.ColumnName);

            if (col is null)
            {
                continue;
            }

            var dir = criterion.Direction == SortDirection.Descending ? "desc" : "asc";
            
            parts.Add($"{col} {dir}");
        }
        
        return string.Join(", ", parts);
    }
    
    public static string? MapPropertyToColumn<TEntity>(string candidate)
    {
        var pi = typeof(TEntity).GetProperty(candidate,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (pi is null) return null;
        return pi.GetCustomAttribute<ColumnAttribute>()?.Name ?? pi.Name;
    }
    
    public static DynamicParameters MergeParams(DynamicParameters? a, DynamicParameters? b)
    {
        var merged = new DynamicParameters();

        if (a != null)
            merged.AddDynamicParams(a);

        if (b != null)
            merged.AddDynamicParams(b);

        return merged;
    }
    
    private static string ReplaceOrder(string sql, string newOrder)
    {
        var matches = RxOrderBy.Matches(sql);
        if (matches.Count == 0) return $"{sql} ORDER BY {newOrder}";
        var last = matches[^1];
        var head = sql[..last.Index].TrimEnd();
        return $"{head} ORDER BY {newOrder}";
    }
    
    private static int FirstIndex(string sql, params Regex[] regs)
    {
        int min = -1;
        foreach (var r in regs)
        {
            var m = r.Match(sql);
            if (!m.Success) continue;
            if (min < 0 || m.Index < min) min = m.Index;
        }
        return min;
    }
    
    private static string TrimSemicolon(string sql) => sql.TrimEnd().TrimEnd(';');
}