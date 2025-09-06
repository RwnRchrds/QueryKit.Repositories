namespace QueryKit.Repositories.Interfaces;

/// <summary>
/// An entity with a primary key.
/// </summary>
public interface IEntity<TKey>
{
    /// <summary>
    /// The primary key of the entity.
    /// </summary>
    TKey Id { get; set; }
}