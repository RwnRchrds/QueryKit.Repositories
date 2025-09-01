using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using QueryKit.Extensions;
using QueryKit.Repositories.Attributes;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Interfaces;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;
using QueryKit.Repositories.Sql;

namespace QueryKit.Repositories;

public class BaseEntityReadRepository<TEntity, TKey> : IEntityReadRepository<TEntity, TKey> where  TEntity : class, IEntity<TKey>
{
    protected readonly IConnectionFactory _factory;

    protected BaseEntityReadRepository(IConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    
    public async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        return await conn.GetAsync<TEntity>(id);
    }

    public async Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var (whereSql, parameters) = QuerySqlBuilder.BuildWhere<TEntity>(filter);

        if (!includeDeleted)
        {
            var (softSql, softParams) = BuildNotDeletedPredicate();
            if (!string.IsNullOrWhiteSpace(softSql))
            {
                whereSql = string.IsNullOrWhiteSpace(whereSql) ? softSql : $"{whereSql} AND {softSql}";
                parameters = QuerySqlBuilder.MergeParams(parameters, softParams);
            }
        }
        
        var orderBy = QuerySqlBuilder.BuildOrderBy<TEntity>(sort);

        if (string.IsNullOrWhiteSpace(orderBy))
        {
            var pk = QuerySqlBuilder.MapPropertyToColumn<TEntity>("Id") ?? "Id";
            orderBy = $"{pk} asc";
        }

        var p = paging ?? new PageOptions();
        return await GetPagedAsync(whereSql, orderBy, parameters, p.PageClamped, p.PageSizeClamped);
    }

    public async Task<bool> IsUniqueIncludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveColumnOrNull(columnName);
        if (resolved is null)
            throw new ArgumentException($"Unknown column '{columnName}' on entity {typeof(TEntity).Name}.", nameof(columnName));

        var (col, _) = resolved.Value;

        using var conn = await OpenConnection();

        string where;
        object? parameters;

        if (value is null)
        {
            where = $"{col} IS NULL";
            parameters = null;
        }
        else
        {
            where = $"{col} = @__val";
            parameters = new { __val = value };
        }

        var count = await conn.RecordCountAsync<TEntity>(where, parameters);
        return count == 0;
    }

    public async Task<bool> IsUniqueExcludingDeletedAsync(string columnName, string? value,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveColumnOrNull(columnName);
        if (resolved is null)
            throw new ArgumentException(
                $"Unknown column '{columnName}' on entity {typeof(TEntity).Name}.",
                nameof(columnName));

        var (col, _) = resolved.Value;

        // Base uniqueness clause
        string where;
        var parameters = new DynamicParameters();

        if (value is null)
        {
            where = $"{col} IS NULL";
        }
        else
        {
            where = $"{col} = @__val";
            parameters.Add("__val", value);
        }

        // Append "not deleted" if we have a [SoftDelete] bool property
        var (softSql, softParams) = BuildNotDeletedPredicate(); // returns (string, DynamicParameters?)
        if (!string.IsNullOrWhiteSpace(softSql))
        {
            where = string.IsNullOrWhiteSpace(where) ? softSql : $"{where} AND {softSql}";
            if (softParams is not null)
                parameters.AddDynamicParams(softParams);
        }

        using var conn = await OpenConnection();
        var count = await conn.RecordCountAsync<TEntity>(where, parameters);
        return count == 0;
    }

    protected async Task<IDbConnection> OpenConnection()
    {
        var conn = _factory.Create();

        if (conn is DbConnection dbConn)
        {
            await dbConn.OpenAsync();
        }
        else
        {
            conn.Open();
        }

        return conn;
    }

    protected async Task<PageResult<TEntity>> GetPagedAsync(string whereSql, string orderBy, object? parameters,
        int page, int pageSize)
    {
        using var conn = await OpenConnection();
        return await InternalGetPagedAsync(conn, whereSql, orderBy, parameters, page, pageSize);
    }

    private static async Task<PageResult<TEntity>> InternalGetPagedAsync(IDbConnection conn, string whereSql,
        string orderBy, object? parameters, int page, int pageSize)
    {
        var items = await conn.GetListPagedAsync<TEntity>(page, pageSize, whereSql, orderBy, parameters);
        var total = await conn.RecordCountAsync<TEntity>(whereSql, parameters);
        return new PageResult<TEntity>
        {
            Items = items.ToList(),
            TotalItems = total
        };
    }
    
    private static (string column, PropertyInfo pi)? ResolveColumnOrNull(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        var pi = typeof(TEntity).GetProperty(candidate,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (pi is null) return null;

        var column = QuerySqlBuilder.MapPropertyToColumn<TEntity>(pi.Name) ?? pi.Name;
        return (column, pi);
    }

    private static bool IsSoftDeleted(TEntity entity)
    {
        var prop = typeof(TEntity).GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<SoftDeleteAttribute>() != null && p.PropertyType == typeof(bool));
        return prop is not null && (bool)(prop.GetValue(entity) ?? false);
    }

    private static (string whereSql, DynamicParameters? parameters) BuildNotDeletedPredicate()
    {
        var prop = typeof(TEntity).GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<SoftDeleteAttribute>() != null && p.PropertyType == typeof(bool));
        if (prop is null) return ("", null);

        var col = QuerySqlBuilder.MapPropertyToColumn<TEntity>(prop.Name) ?? prop.Name;

        var dp = new DynamicParameters();
        dp.Add("__NotDeleted", false);

        return ($"{col} = @__NotDeleted", dp);
    }
}