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
using QueryKit.Sql;

namespace QueryKit.Repositories;

/// <summary>
/// Provides read-only operations for entities using Dapper and QueryKit SQL helpers.
/// Includes convenience methods for executing custom SQL against entity types or DTOs.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TKey">Primary key type.</typeparam>
public class BaseEntityReadRepository<TEntity, TKey> : IBaseEntityReadRepository<TEntity, TKey> where  TEntity : class, IBaseEntity<TKey>
{
    private static readonly Lazy<(string PropertyName, bool HasSoftDelete)> SoftDeleteCache =
        new(() =>
        {
            var prop = typeof(TEntity).GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<SoftDeleteAttribute>() != null
                                     && p.PropertyType == typeof(bool));
            return prop is null ? ("", false) : (prop.Name, true);
        });

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
    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (id is null)
        {
            throw new ArgumentNullException(nameof(id));
        }

        if (EqualityComparer<TKey>.Default.Equals(id, default!))
            throw new ArgumentException("id must not be the default value.", nameof(id));

        using var lease = await AcquireConnection(transaction, cancellationToken);
        return await lease.Connection.GetAsync<TEntity>(id, transaction, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PageResult<TEntity>> GetListPagedAsync(FilterOptions? filter = null, SortOptions? sort = null,
        PageOptions? paging = null, bool includeDeleted = false, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
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
            var idProps = SqlConvention.GetIdProperties(typeof(TEntity));
            var pkCol = (idProps?.Length ?? 0) > 0
                ? QuerySqlBuilder.MapPropertyToColumn<TEntity>(idProps![0].Name) ?? idProps[0].Name
                : (QuerySqlBuilder.MapPropertyToColumn<TEntity>("Id") ?? "Id");
            orderBy = $"{pkCol} asc";
        }

        var p = paging ?? new PageOptions();
        return await GetPagedAsync(whereSql, orderBy, parameters, p.PageClamped, p.PageSizeClamped, cancellationToken, transaction);
    }

    /// <inheritdoc />
    public virtual async Task<IList<TEntity>> GetListAsync(FilterOptions? filter = null, SortOptions? sort = null,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);

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
            var idProps = SqlConvention.GetIdProperties(typeof(TEntity));
            var pkCol = (idProps?.Length ?? 0) > 0
                ? QuerySqlBuilder.MapPropertyToColumn<TEntity>(idProps![0].Name) ?? idProps[0].Name
                : (QuerySqlBuilder.MapPropertyToColumn<TEntity>("Id") ?? "Id");
            orderBy = $"{pkCol} asc";
        }

        var results =
            await lease.Connection.GetListAsync<TEntity>(whereSql, parameters, orderBy, transaction, cancellationToken: cancellationToken);
        return results.ToList();
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsUniqueIncludingDeletedAsync(string columnName, string? value,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
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

        using var lease = await AcquireConnection(transaction, cancellationToken);
        var count = await lease.Connection.RecordCountAsync<TEntity>(where, parameters, transaction, cancellationToken: cancellationToken);
        return count == 0;
    }

    /// <inheritdoc />
    public virtual async Task<bool> IsUniqueExcludingDeletedAsync(string columnName, string? value,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
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

        using var lease = await AcquireConnection(transaction, cancellationToken);
        var count = await lease.Connection.RecordCountAsync<TEntity>(where, parameters, transaction, cancellationToken: cancellationToken);
        return count == 0;
    }

    /// <summary>
    /// Gets a single entity using the provided SQL and parameters.
    /// </summary>
    protected virtual async Task<TEntity?> GetAsync(string sql, object? parameters, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);
        var cmd = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return await lease.Connection.QueryFirstOrDefaultAsync<TEntity>(cmd);
    }

    /// <summary>
    /// Gets a list of entities using the provided SQL and parameters.
    /// </summary>
    protected virtual async Task<IList<TEntity>> GetListAsync(string sql, object? parameters, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);
        var cmd = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        var rows = await lease.Connection.QueryAsync<TEntity>(cmd);
        return rows.ToList();
    }

    /// <summary>
    /// Gets a single record of type <typeparamref name="T"/> using the provided SQL and parameters.
    /// </summary>
    protected virtual async Task<T?> GetAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);
        var cmd = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        return await lease.Connection.QueryFirstOrDefaultAsync<T>(cmd);
    }

    /// <summary>
    /// Gets a list of records of type <typeparamref name="T"/> using the provided SQL and parameters.
    /// </summary>
    protected virtual async Task<IList<T>> GetListAsync<T>(string sql, object? parameters, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);
        var cmd = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        var rows = await lease.Connection.QueryAsync<T>(cmd);
        return rows.ToList();
    }

    /// <summary>
    /// Gets a paged list of records of type <typeparamref name="T"/> using the provided SQL, parameters, and optional filtering, sorting, and paging options.
    /// </summary>
    protected virtual async Task<PageResult<T>> GetListPagedAsync<T>(string sql, object? parameters, FilterOptions? filter, SortOptions? sort, PageOptions? paging,
        bool includeDeleted = false, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        var (whereSql, whereParams) = QuerySqlBuilder.BuildWhere<T>(filter);

        if (!includeDeleted && typeof(T) == typeof(TEntity))
        {
            var (softSql, softParams) = BuildNotDeletedPredicate(DefaultAlias);
            if (!string.IsNullOrWhiteSpace(softSql))
            {
                whereSql = string.IsNullOrWhiteSpace(whereSql) ? softSql : $"{whereSql} AND {softSql}";
                whereParams = QuerySqlBuilder.MergeParams(whereParams, softParams);
            }
        }

        var orderBy = QuerySqlBuilder.BuildOrderBy<T>(sort);

        var dp = new DynamicParameters();
        if (parameters is not null) dp.AddDynamicParams(parameters);
        dp.AddDynamicParams(whereParams);

        var withWhere = QuerySqlBuilder.InjectWhere(sql, whereSql);

        var withOrder = QuerySqlBuilder.ReplaceOrder(withWhere, orderBy);

        var querySql = paging is not null
            ? QuerySqlBuilder.AppendPaging(withOrder, paging)
            : withOrder;

        var countSql = $"SELECT COUNT(1) FROM ({QuerySqlBuilder.StripTrailingOrder(withOrder)}) q";

        using var lease = await AcquireConnection(transaction, cancellationToken);
        var dataCmd  = new CommandDefinition(querySql, dp, transaction, cancellationToken: cancellationToken);
        var countCmd = new CommandDefinition(countSql, dp, transaction, cancellationToken: cancellationToken);

        var items = (await lease.Connection.QueryAsync<T>(dataCmd)).ToList();
        var total = await lease.Connection.ExecuteScalarAsync<int>(countCmd);

        return new PageResult<T> { Items = items, TotalItems = total };
    }

    /// <summary>
    /// Gets a paged list of records of type <typeparamref name="T"/> using the provided SQL for data retrieval and counting, along with parameters and paging options.
    /// </summary>
    protected virtual async Task<PageResult<T>> GetListPagedAsync<T>(
        string dataSql,
        string countSql,
        object? parameters,
        PageOptions paging,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        var dp = new DynamicParameters(parameters);
        var pagedSql = QuerySqlBuilder.AppendPaging(dataSql, paging);

        using var lease = await AcquireConnection(transaction, cancellationToken);
        var dataCmd  = new CommandDefinition(pagedSql, dp, transaction, cancellationToken: cancellationToken);
        var countCmd = new CommandDefinition(countSql, dp, transaction, cancellationToken: cancellationToken);

        var items = (await lease.Connection.QueryAsync<T>(dataCmd)).ToList();
        var total = await lease.Connection.ExecuteScalarAsync<int>(countCmd);

        return new PageResult<T> { Items = items, TotalItems = total };
    }

    /// <summary>
    /// Opens a new database connection using <see cref="_factory"/> and ensures it's open.
    /// Override to customize connection behavior.
    /// </summary>
    /// <returns>An open <see cref="IDbConnection"/>.</returns>
    protected virtual async Task<IDbConnection> OpenConnection(CancellationToken cancellationToken = default)
    {
        var conn = _factory.Create();

        try
        {
            if (conn is DbConnection dbConn)
            {
                await dbConn.OpenAsync(cancellationToken);
            }
            else
            {
                conn.Open();
            }
        }
        catch
        {
            conn.Dispose();
            throw;
        }

        return conn;
    }

    /// <summary>
    /// Acquires a connection for the duration of one operation.
    /// If <paramref name="transaction"/> is supplied, the transaction's connection is borrowed and
    /// the returned <see cref="ConnectionLease"/> will not dispose it. Otherwise a new connection
    /// is opened via <see cref="OpenConnection"/> and disposed when the lease is disposed.
    /// </summary>
    protected async Task<ConnectionLease> AcquireConnection(IDbTransaction? transaction, CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            var borrowed = transaction.Connection
                ?? throw new InvalidOperationException("The supplied transaction has no associated connection.");
            return new ConnectionLease(borrowed, ownsConnection: false);
        }

        var opened = await OpenConnection(cancellationToken);
        return new ConnectionLease(opened, ownsConnection: true);
    }

    /// <summary>
    /// Gets a paged list of <typeparamref name="TEntity"/> using the provided WHERE clause, ORDER BY clause, parameters, and paging options.
    /// </summary>
    protected virtual async Task<PageResult<TEntity>> GetPagedAsync(string whereSql, string orderBy, object? parameters,
        int page, int pageSize, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);
        var items = await lease.Connection.GetListPagedAsync<TEntity>(page, pageSize, whereSql, orderBy,
            parameters, transaction, cancellationToken: cancellationToken);
        var total = await lease.Connection.RecordCountAsync<TEntity>(whereSql, parameters, transaction, cancellationToken: cancellationToken);
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

    private static (string whereSql, DynamicParameters? parameters) BuildNotDeletedPredicate(string? alias = null)
    {
        var cached = SoftDeleteCache.Value;
        if (!cached.HasSoftDelete) return ("", null);

        var parms = new DynamicParameters();
        parms.Add("__qkNotDeleted", false);

        var conv = QuerySqlBuilder.CreateConvention();

        // col is already encapsulated for the CURRENT dialect
        var col = QuerySqlBuilder.MapPropertyToColumn<TEntity>(cached.PropertyName)
                  ?? conv.Encapsulate(cached.PropertyName);

        // If the mapped column already contains a dot (schema/table/alias qualification),
        // don't try to prefix another alias.
        var qualifiedCol = (!string.IsNullOrWhiteSpace(alias) && !col.Contains('.'))
            ? $"{conv.Encapsulate(alias)}.{col}"
            : col;

        return ($"{qualifiedCol} = @__qkNotDeleted", parms);
    }

    /// <summary>
    /// A leased connection. If the lease was opened by the repository it disposes the connection;
    /// if it was borrowed from a caller-supplied transaction the connection is left intact.
    /// </summary>
    protected readonly struct ConnectionLease : IDisposable
    {
        private readonly IDbConnection _connection;
        private readonly bool _ownsConnection;

        /// <summary>The leased connection. Always open.</summary>
        public IDbConnection Connection => _connection;

        internal ConnectionLease(IDbConnection connection, bool ownsConnection)
        {
            _connection = connection;
            _ownsConnection = ownsConnection;
        }

        /// <summary>Disposes the connection if it was opened by the lease; otherwise no-op.</summary>
        public void Dispose()
        {
            if (_ownsConnection) _connection.Dispose();
        }
    }
}
