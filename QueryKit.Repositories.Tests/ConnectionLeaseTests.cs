using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using QueryKit.Repositories;
using QueryKit.Repositories.Interfaces;

namespace QueryKit.Repositories.Tests;

public class ConnectionLeaseTests
{
    private sealed class TestEntity : IBaseEntity<int>
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// Exposes the protected <c>AcquireConnection</c> for testing.
    /// </summary>
    private sealed class TestRepo : BaseEntityReadRepository<TestEntity, int>
    {
        public TestRepo(IConnectionFactory factory) : base(factory) { }

        public Task<object> AcquireForTestAsync(IDbTransaction? tx, CancellationToken ct = default)
            => AcquireBoxed(tx, ct);

        // The lease type is a protected nested struct; box it so the test can call Dispose without
        // needing visibility into the type itself.
        private async Task<object> AcquireBoxed(IDbTransaction? tx, CancellationToken ct)
            => await AcquireConnection(tx, ct);
    }

    private sealed class StubFactory : IConnectionFactory
    {
        public StubConnection Created { get; } = new();
        public IDbConnection Create() => Created;
    }

    private sealed class StubConnection : IDbConnection
    {
        public bool Disposed { get; private set; }
        public bool Opened { get; set; }

        public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "";
        public ConnectionState State => Opened && !Disposed ? ConnectionState.Open : ConnectionState.Closed;

        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        public IDbTransaction BeginTransaction(IsolationLevel il) => throw new NotSupportedException();
        public void ChangeDatabase(string databaseName) { }
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() => Opened = true;
        public void Dispose() => Disposed = true;
    }

    private sealed class StubTransaction : IDbTransaction
    {
        public StubTransaction(IDbConnection? connection) { Connection = connection; }
        public IDbConnection? Connection { get; }
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public void Commit() { }
        public void Rollback() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task AcquireConnection_NoTransaction_OpensAndDisposesOnLeaseDispose()
    {
        var factory = new StubFactory();
        var repo = new TestRepo(factory);

        var lease = await repo.AcquireForTestAsync(tx: null);
        Assert.True(factory.Created.Opened);
        Assert.False(factory.Created.Disposed);

        ((IDisposable)lease).Dispose();
        Assert.True(factory.Created.Disposed);
    }

    [Fact]
    public async Task AcquireConnection_WithTransaction_BorrowsAndDoesNotDispose()
    {
        var borrowed = new StubConnection { Opened = true };
        var tx = new StubTransaction(borrowed);
        var factory = new StubFactory();
        var repo = new TestRepo(factory);

        var lease = await repo.AcquireForTestAsync(tx);
        Assert.False(factory.Created.Opened, "Factory should not have been used when a transaction is supplied.");
        ((IDisposable)lease).Dispose();
        Assert.False(borrowed.Disposed, "Borrowed connection must not be disposed by the lease.");
    }

    [Fact]
    public async Task AcquireConnection_TransactionWithoutConnection_Throws()
    {
        var tx = new StubTransaction(connection: null);
        var repo = new TestRepo(new StubFactory());

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.AcquireForTestAsync(tx));
    }
}
