using System.Threading;
using System.Threading.Tasks;

namespace QueryKit.Repositories.Interfaces;

public interface IEntityRepository<TEntity, TKey> : IEntityReadRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// Inserts a new entity and returns it with its key populated.
    /// </summary>
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing entity and returns the updated instance.
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Inserts or updates an entity based on whether its key has the default value.
    /// </summary>
    Task<TEntity> InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an entity by primary key. If a boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// the entity is soft-deleted instead.
    /// </summary>
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes or soft-deletes the provided entity instance.
    /// </summary>
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Undeletes soft-deleted entities
    /// </summary>
    Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default);
}