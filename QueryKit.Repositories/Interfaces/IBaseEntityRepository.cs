using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Repositories.Exceptions;

namespace QueryKit.Repositories.Interfaces;

/// <summary>
/// A repository interface for managing entities with CRUD operations.
/// </summary>
public interface IBaseEntityRepository<TEntity, TKey> : IBaseEntityReadRepository<TEntity, TKey> where TEntity : class, IBaseEntity<TKey>
{
    /// <summary>
    /// Inserts a new entity and returns the inserted instance with its primary key populated.
    /// </summary>
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);

    /// <summary>
    /// Inserts many entities in as few statements as the dialect allows, and returns the number of
    /// rows written. Identity keys are permitted only when <paramref name="discardGeneratedKeys"/>
    /// is set, because the keys the database generates cannot be read back for many rows at once
    /// and the entities would otherwise come back with their keys still unset.
    /// </summary>
    Task<int> BatchInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null, int? batchSize = null, bool discardGeneratedKeys = false);

    /// <summary>
    /// Updates many entities by key in one command per batch, and returns the number of rows
    /// affected. Writes the same columns <see cref="UpdateAsync"/> does.
    /// </summary>
    /// <remarks>
    /// This neither writes nor checks a version column. Use <see cref="UpdateWithVersionAsync"/>
    /// per entity where optimistic concurrency matters, because a batch cannot report which row of
    /// many lost the race.
    /// </remarks>
    Task<int> BatchUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null, int? batchSize = null);

    /// <summary>
    /// Inserts entities, overwriting any whose key is already present, and returns the number of
    /// rows the statement reported. The table needs a unique index or primary key over the entity's
    /// key: without one every dialect inserts a duplicate and reports success.
    /// </summary>
    Task<int> UpsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null, int? batchSize = null);

    /// <summary>
    /// Updates an existing entity and returns the updated instance.
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);

    /// <summary>
    /// Updates an existing entity with optimistic concurrency control.
    /// The update will only succeed if the current version of the entity matches the expected version.
    /// If the update fails due to a version mismatch, a <see cref="ConcurrencyException"/> will be thrown.
    /// </summary>
    Task<TEntity> UpdateWithVersionAsync(TEntity entity, long expectedVersion,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null);

    /// <summary>
    /// Inserts a new entity if it is new, otherwise updates the existing entity.
    /// </summary>
    Task<TEntity> InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);

    /// <summary>
    /// Deletes an entity by primary key. If a boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// the entity is soft-deleted instead.
    /// </summary>
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default, bool softDelete = true,
        IDbTransaction? transaction = null);

    /// <summary>
    /// Deletes an entity. If a boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// the entity is soft-deleted instead.
    /// </summary>
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default, bool softDelete = true,
        IDbTransaction? transaction = null);

    /// <summary>
    /// Undeletes a soft-deleted entity by primary key. If no boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// this method does nothing and returns false.
    /// </summary>
    Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default, IDbTransaction? transaction = null);
}
