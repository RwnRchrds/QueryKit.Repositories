using System.Threading;
using System.Threading.Tasks;

namespace QueryKit.Repositories.Interfaces;

public interface IEntityRepository<TEntity, TKey> : IEntityReadRepository<TEntity, TKey> where TEntity : class, IEntity<TKey>
{
    Task<TEntity> InsertAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<TEntity> InsertOrUpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<bool> UndeleteAsync(TKey id, CancellationToken cancellationToken = default);
}