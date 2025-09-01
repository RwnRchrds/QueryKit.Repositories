namespace QueryKit.Repositories.Interfaces;

public interface IEntity<TKey>
{
    TKey Id { get; set; }
}