using System.Threading;
using System.Threading.Tasks;

namespace QueryKit.Repositories.Interfaces;

public interface IEntityRepository<TEntity, TKey> : IEntityReadRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// Inserts a new entity and returns the inserted instance with its primary key populated.
    /// </summary>
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing entity and returns the updated instance.
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Inserts a new entity if it is new, otherwise updates the existing entity.
    /// </summary>
    Task<TEntity> InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an entity by primary key. If a boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// the entity is soft-deleted instead.
    /// </summary>
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an entity. If a boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// the entity is soft-deleted instead.
    /// </summary>
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Undeletes a soft-deleted entity by primary key. If no boolean property has <see cref="Attributes.SoftDeleteAttribute"/>,
    /// this method does nothing and returns false.
    /// </summary>
    Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default);
}