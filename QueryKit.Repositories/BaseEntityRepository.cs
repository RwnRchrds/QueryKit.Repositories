using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Extensions;
using QueryKit.Repositories.Attributes;
using QueryKit.Repositories.Exceptions;
using QueryKit.Repositories.Interfaces;

namespace QueryKit.Repositories;

/// <summary>
/// Provides read/write operations for entities, including soft delete support via <see cref="SoftDeleteAttribute"/>.
/// Exposes lifecycle hooks for insert, update, and delete operations that can be overridden by derived repositories.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TKey">Primary key type.</typeparam>
public class BaseEntityRepository<TEntity, TKey> : BaseEntityReadRepository<TEntity, TKey>,
    IBaseEntityRepository<TEntity, TKey> where TEntity : class, IBaseEntity<TKey>
{
    private static readonly PropertyInfo? SoftDeleteProp =
        typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p =>
                p.CanWrite &&
                p.GetCustomAttribute<SoftDeleteAttribute>() != null &&
                p.PropertyType == typeof(bool));

    /// <summary>
    /// Creates a new instance of <see cref="BaseEntityRepository{TEntity, TKey}"/>.
    /// </summary>
    protected BaseEntityRepository(IConnectionFactory factory) : base(factory)
    {
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);

        var result = await lease.Connection.InsertAsync<TKey, TEntity>(entity, transaction, cancellationToken: cancellationToken);

        if (result != null) entity.Id = result;

        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<int> BatchInsertAsync(IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default, IDbTransaction? transaction = null,
        int? batchSize = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);

        return await lease.Connection.BatchInsertAsync(entities, transaction, batchSize: batchSize,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);

        await lease.Connection.UpdateAsync(entity, transaction, cancellationToken: cancellationToken);

        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> UpdateWithVersionAsync(
        TEntity entity, long expectedVersion, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        using var lease = await AcquireConnection(transaction, cancellationToken);

        var rows = await lease.Connection.UpdateWithVersionAsync(entity, expectedVersion, transaction, cancellationToken: cancellationToken);
        if (rows == 0) throw new ConcurrencyException("No rows were updated. The entity may have been modified or deleted.");

        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> InsertOrUpdateAsync(TEntity entity,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        if (IsNewEntity(entity))
        {
            return await InsertAsync(entity, cancellationToken, transaction);
        }

        return await UpdateAsync(entity, cancellationToken, transaction);
    }

    /// <inheritdoc/>
    public virtual async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default,
        bool softDelete = true, IDbTransaction? transaction = null)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
        {
            throw new ArgumentException("id must not be the default value.", nameof(id));
        }

        using var lease = await AcquireConnection(transaction, cancellationToken);

        var entity = await lease.Connection.GetAsync<TEntity?>(id, transaction, cancellationToken: cancellationToken);

        if (entity is null)
        {
            return false;
        }

        if (!softDelete || SoftDeleteProp is null)
        {
            var affected = await lease.Connection.DeleteAsync<TEntity>(id, transaction, cancellationToken: cancellationToken);
            return affected > 0;
        }

        SoftDeleteProp.SetValue(entity, true);

        var rows = await lease.Connection.UpdateAsync(entity, transaction, cancellationToken: cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc/>
    public virtual async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default,
        bool softDelete = true, IDbTransaction? transaction = null)
    {
        return await DeleteAsync(entity.Id, cancellationToken, softDelete, transaction);
    }

    /// <inheritdoc/>
    public virtual async Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default, IDbTransaction? transaction = null)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
        {
            throw new ArgumentException("id must not be the default value.", nameof(id));
        }

        if (SoftDeleteProp is null)
            return false;

        using var lease = await AcquireConnection(transaction, cancellationToken);
        var entity = await lease.Connection.GetAsync<TEntity?>(id, transaction, cancellationToken: cancellationToken);
        if (entity is null) return false;

        SoftDeleteProp.SetValue(entity, false);
        var rows = await lease.Connection.UpdateAsync(entity, transaction, cancellationToken: cancellationToken);
        return rows > 0;
    }

    /// <summary>
    /// Gets whether the entity is new (i.e., its primary key is the default value).
    /// </summary>
    protected bool IsNewEntity(TEntity entity)
    {
        return EqualityComparer<TKey>.Default.Equals(entity.Id, default!);
    }
}
