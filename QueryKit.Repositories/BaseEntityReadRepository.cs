using System;
using System.Collections.Generic;
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

/// <summary>
/// Provides read-only operations for entities using Dapper and QueryKit SQL helpers.
/// Includes convenience methods for executing custom SQL against entity types or DTOs.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TKey">Primary key type.</typeparam>
public class BaseEntityReadRepository<TEntity, TKey> : IEntityReadRepository<TEntity, TKey> where  TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// Connection factory used to open database connections.
    /// </summary>
    protected readonly IConnectionFactory _factory;
    
    /// <summary>
    /// If your custom base SQL uses a table alias for the entity table (e.g., <c>FROM Students s</c>),
    /// override this to have soft-delete predicates qualified as <c>s.IsDeleted = 0</c>.
    /// </summary>
    protected virtual string? DefaultAlias => null;

    /// <summary>
    /// Initializes the repository with the provided <paramref name="factory"/>.
    /// </summary>
    /// <param name="factory">Connection factory used to create connections per operation.</param>
    protected BaseEntityReadRepository(IConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
    
    /// <inheritdoc />
    public async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        return await conn.GetAsync<TEntity>(id);
    }

    /// <inheritdoc />
    public async Task<TEntity?> GetAsync(string sql, object? parameters, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<TEntity>(cmd);
    }

    /// <inheritdoc />
    public async Task<IList<TEntity>> GetListAsync(string sql, object? parameters, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var rows = await conn.QueryAsync<TEntity>(cmd);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var (whereSql, parameters) = QuerySqlBuilder.BuildWhere<TEntity>(filter);

        if (!includeDeleted)
        {
            var (softSql, softParams) = BuildNotDeletedPredicate(DefaultAlias);
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
        return await GetPagedAsync(whereSql, orderBy, parameters, p.PageClamped, p.PageSizeClamped, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsUniqueIncludingDeletedAsync(string columnName, string value,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveColumnOrNull(columnName);
        if (resolved is null)
            throw new ArgumentException($"Unknown column '{columnName}' on entity {typeof(TEntity).Name}.", nameof(columnName));

        var (col, _) = resolved.Value;

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

        using var conn = await OpenConnection();
        var count = await conn.RecordCountAsync<TEntity>(where, parameters);
        return count == 0;
    }

    /// <inheritdoc />
    public async Task<bool> IsUniqueExcludingDeletedAsync(string columnName, string? value,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveColumnOrNull(columnName);
        if (resolved is null)
            throw new ArgumentException(
                $"Unknown column '{columnName}' on entity {typeof(TEntity).Name}.",
                nameof(columnName));

        var (col, _) = resolved.Value;

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

        var (softSql, softParams) = BuildNotDeletedPredicate(DefaultAlias);
        if (!string.IsNullOrWhiteSpace(softSql))
        {
            where = string.IsNullOrWhiteSpace(where) ? softSql : $"{where} AND {softSql}";
            if (softParams is not null)
                parameters.AddDynamicParams(softParams);
        }

        using var conn = await OpenConnection();
        var cmd = new CommandDefinition($"SELECT 1", cancellationToken: cancellationToken);
        var count = await conn.RecordCountAsync<TEntity>(where, parameters);
        return count == 0;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<T>(cmd);
    }

    /// <inheritdoc />
    public async Task<IList<T>> GetListAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
        var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var rows = await conn.QueryAsync<T>(cmd);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<PageResult<T>> GetListPagedAsync<T>(string sql, object? parameters, FilterOptions? filter, SortOptions? sort, PageOptions? paging,
        CancellationToken cancellationToken = default)
    {
        paging ??= new PageOptions();
        
        var (whereSql, whereParams) = QuerySqlBuilder.BuildWhere<T>(filter);
        
        var (softSql, softParams) = BuildNotDeletedPredicate(DefaultAlias);
        if (!string.IsNullOrWhiteSpace(softSql))
        {
            whereSql = string.IsNullOrWhiteSpace(whereSql) ? softSql : $"{whereSql} AND {softSql}";
            whereParams = QuerySqlBuilder.MergeParams(whereParams, softParams);
        }
        
        var orderBy = QuerySqlBuilder.BuildOrderBy<T>(sort);
        
        var dp = new DynamicParameters();
        if (parameters  is not null) dp.AddDynamicParams(parameters);
        if (whereParams is not null) dp.AddDynamicParams(whereParams);
        
        var withWhere = QuerySqlBuilder.InjectWhere(sql, whereSql);
        var withOrder = QuerySqlBuilder.ReplaceOrder(withWhere, orderBy);
        
        var countSql = $"SELECT COUNT(1) FROM ({QuerySqlBuilder.StripTrailingOrder(withOrder)}) q";
        
        var pagedSql = QuerySqlBuilder.AppendPaging(withOrder, paging);

        using var conn = await OpenConnection();
        var dataCmd  = new CommandDefinition(pagedSql, dp, cancellationToken: cancellationToken);
        var countCmd = new CommandDefinition(countSql, dp, cancellationToken: cancellationToken);

        var items = (await conn.QueryAsync<T>(dataCmd)).ToList();
        var total = await conn.ExecuteScalarAsync<int>(countCmd);

        return new PageResult<T> { Items = items, TotalItems = total };
    }
    
    /// <inheritdoc />
    public async Task<PageResult<T>> GetListPagedAsync<T>(
        string dataSql,
        string countSql,
        object? parameters,
        PageOptions paging,
        CancellationToken cancellationToken = default)
    {
        var dp = new DynamicParameters(parameters);
        var pagedSql = QuerySqlBuilder.AppendPaging(dataSql, paging);

        using var conn = await OpenConnection();
        var dataCmd  = new CommandDefinition(pagedSql, dp, cancellationToken: cancellationToken);
        var countCmd = new CommandDefinition(countSql, dp, cancellationToken: cancellationToken);

        var items = (await conn.QueryAsync<T>(dataCmd)).ToList();
        var total = await conn.ExecuteScalarAsync<int>(countCmd);

        return new PageResult<T> { Items = items, TotalItems = total };
    }

    /// <summary>
    /// Opens a new database connection using <see cref="_factory"/> and ensures it's open.
    /// Override to customize connection behavior.
    /// </summary>
    /// <returns>An open <see cref="IDbConnection"/>.</returns>
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
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection();
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

    private static (string whereSql, DynamicParameters? parameters) BuildNotDeletedPredicate(string? alias = null)
    {
        var prop = typeof(TEntity).GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<SoftDeleteAttribute>() != null && p.PropertyType == typeof(bool));
        if (prop is null) return ("", null);

        var col = QuerySqlBuilder.MapPropertyToColumn<TEntity>(prop.Name) ?? prop.Name;
        var qualified = string.IsNullOrWhiteSpace(alias) ? col : $"{alias}.{col}";

        var dp = new DynamicParameters();
        dp.Add("__NotDeleted", false);

        return ($"{qualified} = @__NotDeleted", dp);
    }
}