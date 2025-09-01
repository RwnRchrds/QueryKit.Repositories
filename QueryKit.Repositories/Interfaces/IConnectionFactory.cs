using System.Data;

namespace QueryKit.Repositories.Interfaces;

public interface IConnectionFactory
{
    IDbConnection Create();
}