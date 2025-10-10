using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Extensions;
using QueryKit.Repositories.Attributes;
using QueryKit.Repositories.Interfaces;

namespace QueryKit.Repositories;

/// <summary>
/// Provides read/write operations for entities, including soft delete support via <see cref="SoftDeleteAttribute"/>.
/// Exposes lifecycle hooks for insert, update, and delete operations that can be overridden by derived repositories.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
/// <typeparam name="TKey">Primary key type.</typeparam>
public class BaseEntityRepository<TEntity, TKey> : BaseEntityReadRepository<TEntity, TKey>, IBaseEntityRepository<TEntity, TKey> where TEntity : class, IBaseEntity<TKey>
{
    private static readonly PropertyInfo? SoftDeleteProp =
        typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<SoftDeleteAttribute>() != null
                                 && p.PropertyType == typeof(bool));
    
    /// <summary>
    /// Creates a new instance of <see cref="BaseEntityRepository{TEntity, TKey}"/>.
    /// </summary>
    protected BaseEntityRepository(IConnectionFactory factory) : base(factory)
    {
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection(cancellationToken);
        
        var result = await conn.InsertAsync<TKey, TEntity>(entity, cancellationToken: cancellationToken);

        if (result != null) entity.Id = result;
        
        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        using var conn = await OpenConnection(cancellationToken);

        await conn.UpdateAsync(entity, cancellationToken: cancellationToken);
        
        return entity;
    }

    /// <inheritdoc/>
    public virtual async Task<TEntity> InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (IsNewEntity(entity))
        {
            return await InsertAsync(entity, cancellationToken);
        }
        
        return await UpdateAsync(entity, cancellationToken);
    }

    /// <inheritdoc/>
    public virtual async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
        {
            throw new ArgumentException("id must not be the default value.", nameof(id));
        }

        using var conn = await OpenConnection(cancellationToken);
        
        var entity = await conn.GetAsync<TEntity?>(id);

        if (entity is null)
        {
            return false;
        }

        if (SoftDeleteProp is null)
        {
            var affected = await conn.DeleteAsync<TEntity>(id, cancellationToken: cancellationToken);
            return affected > 0;
        }
        
        SoftDeleteProp.SetValue(entity, true);
        
        var rows = await conn.UpdateAsync(entity, cancellationToken: cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(entity.Id, cancellationToken);
    }
    
    /// <inheritdoc/>
    public async Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        if (EqualityComparer<TKey>.Default.Equals(id, default!))
        {
            throw new ArgumentException("id must not be the default value.", nameof(id));
        }
        
        if (SoftDeleteProp is null)
            return false;

        using var conn = await OpenConnection(cancellationToken);
        var entity = await conn.GetAsync<TEntity?>(id);
        if (entity is null) return false;

        SoftDeleteProp.SetValue(entity, false);
        var rows = await conn.UpdateAsync(entity);
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