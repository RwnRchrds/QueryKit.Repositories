using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using QueryKit.Attributes;
using QueryKit.Dialects;
using QueryKit.Extensions;
using QueryKit.Repositories.Attributes;
using QueryKit.Repositories.Exceptions;
using QueryKit.Repositories.Interfaces;

namespace QueryKit.Repositories.Tests;

/// <summary>
/// BaseEntityRepository is the type consumers derive from, and most of what it does is not
/// delegation: it chooses between a soft delete and a hard one, decides whether an entity is new,
/// writes a generated key back onto the instance, and turns an unmatched versioned update into a
/// ConcurrencyException. None of that is reachable from QueryKit's own tests, which stop at the
/// connection extensions. These run the real thing against SQLite.
/// </summary>
public class BaseEntityRepositoryTests : IDisposable
{
    [Table("Widgets")]
    private sealed class Widget : IBaseEntity<Guid>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int Quantity { get; set; }

        [SoftDelete]
        public bool Deleted { get; set; }
    }

    // No soft-delete property, so a delete must remove the row whatever the caller asks for.
    [Table("Gadgets")]
    private sealed class Gadget : IBaseEntity<Guid>
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }

    [Table("VersionedWidgets")]
    private sealed class VersionedWidget : IBaseEntity<Guid>
    {
        public Guid Id { get; set; }
        public long Version { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class WidgetRepository(IConnectionFactory factory)
        : BaseEntityRepository<Widget, Guid>(factory);

    private sealed class GadgetRepository(IConnectionFactory factory)
        : BaseEntityRepository<Gadget, Guid>(factory);

    private sealed class VersionedRepository(IConnectionFactory factory)
        : BaseEntityRepository<VersionedWidget, Guid>(factory);

    /// <summary>
    /// A shared-cache in-memory database, so every connection the factory hands out sees the same
    /// tables. A plain ":memory:" gives each connection a private empty database.
    /// </summary>
    private sealed class SqliteFactory : IConnectionFactory, IDisposable
    {
        private readonly SqliteConnection _keepAlive;

        private string ConnectionString { get; }

        public SqliteFactory()
        {
            ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
            _keepAlive.Execute(
                "CREATE TABLE Widgets (Id TEXT PRIMARY KEY, Name TEXT NOT NULL, " +
                "Quantity INTEGER NOT NULL, Deleted INTEGER NOT NULL DEFAULT 0);" +
                "CREATE TABLE Gadgets (Id TEXT PRIMARY KEY, Name TEXT NOT NULL);" +
                "CREATE TABLE VersionedWidgets (Id TEXT PRIMARY KEY, Version INTEGER NOT NULL, " +
                "Name TEXT NOT NULL);");
        }

        public IDbConnection Create() => new SqliteConnection(ConnectionString);

        public IDbConnection Opened()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        public void Dispose() => _keepAlive.Dispose();
    }

    /// <summary>
    /// SQLite has no Guid type, so a key round-trips as TEXT and Dapper will not cast it back on
    /// its own. Registering this is what a consumer on SQLite has to do too.
    /// </summary>
    private sealed class SqliteGuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override void SetValue(IDbDataParameter parameter, Guid value)
            => parameter.Value = value.ToString();

        public override Guid Parse(object value)
            => value is Guid g ? g : Guid.Parse((string)value);
    }

    private static bool _handlerRegistered;

    private readonly SqliteFactory _factory = new();
    private readonly WidgetRepository _widgets;
    private readonly GadgetRepository _gadgets;
    private readonly VersionedRepository _versioned;

    public BaseEntityRepositoryTests()
    {
        if (!_handlerRegistered)
        {
            SqlMapper.AddTypeHandler(new SqliteGuidHandler());
            _handlerRegistered = true;
        }

        ConnectionExtensions.UseDialect(Dialect.SQLite);
        _widgets = new WidgetRepository(_factory);
        _gadgets = new GadgetRepository(_factory);
        _versioned = new VersionedRepository(_factory);
    }

    public void Dispose()
    {
        _factory.Dispose();
        ConnectionExtensions.UseDialect(Dialect.SQLServer);
    }

    private static Widget NewWidget(string name = "Sprocket", int quantity = 1) =>
        new() { Name = name, Quantity = quantity };

    private T Scalar<T>(string sql, object? param = null)
    {
        using var conn = _factory.Opened();
        return conn.ExecuteScalar<T>(sql, param)!;
    }

    private static object Key(Guid id) => new { Id = id };

    // ------------------------------------------------------------------------------- insert

    [Fact]
    public async Task InsertPutsTheGeneratedKeyBackOnTheEntity()
    {
        var widget = NewWidget();

        var returned = await _widgets.InsertAsync(widget);

        Assert.NotEqual(Guid.Empty, widget.Id);
        Assert.Same(widget, returned);
        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    [Fact]
    public async Task InsertKeepsAKeyTheCallerChose()
    {
        var chosen = Guid.NewGuid();

        await _widgets.InsertAsync(new Widget { Id = chosen, Name = "Cog" });

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM Widgets WHERE Id = @Id", Key(chosen)));
    }

    [Fact]
    public async Task BatchInsertWritesEveryRow()
    {
        var widgets = Enumerable.Range(0, 30).Select(i => NewWidget("W" + i, i)).ToArray();

        var written = await _widgets.BatchInsertAsync(widgets);

        Assert.Equal(30, written);
        Assert.Equal(30, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
        Assert.Equal(30, widgets.Select(w => w.Id).Distinct().Count());
    }

    [Fact]
    public async Task BatchInsertRollsBackWithItsTransaction()
    {
        using var conn = _factory.Opened();
        using (var tx = conn.BeginTransaction())
        {
            await _widgets.BatchInsertAsync(
                Enumerable.Range(0, 5).Select(_ => NewWidget()), transaction: tx);
            tx.Rollback();
        }

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
    }

    // ---------------------------------------------------------------------- insert or update

    [Fact]
    public async Task InsertOrUpdateInsertsWhenTheKeyIsUnset()
    {
        await _widgets.InsertOrUpdateAsync(NewWidget("Flange"));

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
    }

    [Fact]
    public async Task InsertOrUpdateUpdatesWhenTheKeyIsSet()
    {
        var widget = await _widgets.InsertAsync(NewWidget("Flange", 2));
        widget.Quantity = 9;

        await _widgets.InsertOrUpdateAsync(widget);

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
        Assert.Equal(9, Scalar<int>("SELECT Quantity FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    // ------------------------------------------------------------------------------- delete

    [Fact]
    public async Task DeleteSetsTheSoftDeleteFlagAndKeepsTheRow()
    {
        var widget = await _widgets.InsertAsync(NewWidget());

        Assert.True(await _widgets.DeleteAsync(widget.Id));

        Assert.Equal(1, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
        Assert.Equal(1, Scalar<int>("SELECT Deleted FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    [Fact]
    public async Task DeleteRemovesTheRowWhenSoftDeleteIsDeclined()
    {
        var widget = await _widgets.InsertAsync(NewWidget());

        Assert.True(await _widgets.DeleteAsync(widget.Id, softDelete: false));

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM Widgets"));
    }

    [Fact]
    public async Task DeleteRemovesTheRowWhenTheEntityHasNoSoftDeleteColumn()
    {
        var gadget = await _gadgets.InsertAsync(new Gadget { Name = "Widget-adjacent" });

        // softDelete: true is the default, and must not be honoured where it cannot be.
        Assert.True(await _gadgets.DeleteAsync(gadget.Id));

        Assert.Equal(0, Scalar<int>("SELECT COUNT(*) FROM Gadgets"));
    }

    [Fact]
    public async Task DeleteReturnsFalseForAnIdThatIsNotThere()
    {
        Assert.False(await _widgets.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DeleteRejectsTheDefaultKey()
    {
        // Without this guard a default key would act on whatever happens to match it.
        await Assert.ThrowsAsync<ArgumentException>(() => _widgets.DeleteAsync(Guid.Empty));
    }

    [Fact]
    public async Task DeletingByEntityMatchesDeletingById()
    {
        var widget = await _widgets.InsertAsync(NewWidget());

        Assert.True(await _widgets.DeleteAsync(widget));

        Assert.Equal(1, Scalar<int>("SELECT Deleted FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    // ----------------------------------------------------------------------------- undelete

    [Fact]
    public async Task UndeleteClearsTheFlag()
    {
        var widget = await _widgets.InsertAsync(NewWidget());
        await _widgets.DeleteAsync(widget.Id);

        Assert.True(await _widgets.UndeleteAsync(widget.Id));

        Assert.Equal(0, Scalar<int>("SELECT Deleted FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    [Fact]
    public async Task UndeleteReturnsFalseWhenTheEntityCannotBeSoftDeleted()
    {
        var gadget = await _gadgets.InsertAsync(new Gadget { Name = "Cog" });

        Assert.False(await _gadgets.UndeleteAsync(gadget.Id));
    }

    [Fact]
    public async Task UndeleteReturnsFalseForAnIdThatIsNotThere()
    {
        Assert.False(await _widgets.UndeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UndeleteRejectsTheDefaultKey()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _widgets.UndeleteAsync(Guid.Empty));
    }

    // ------------------------------------------------------------------------------- update

    [Fact]
    public async Task UpdatePersistsTheChange()
    {
        var widget = await _widgets.InsertAsync(NewWidget("Sprocket", 3));
        widget.Name = "Renamed";
        widget.Quantity = 7;

        await _widgets.UpdateAsync(widget);

        Assert.Equal("Renamed", Scalar<string>("SELECT Name FROM Widgets WHERE Id = @Id", Key(widget.Id)));
        Assert.Equal(7, Scalar<int>("SELECT Quantity FROM Widgets WHERE Id = @Id", Key(widget.Id)));
    }

    [Fact]
    public async Task UpdateWithVersionSucceedsAndBumpsTheVersionWhenItMatches()
    {
        var widget = await _versioned.InsertAsync(new VersionedWidget { Name = "First", Version = 1 });
        widget.Name = "Second";

        await _versioned.UpdateWithVersionAsync(widget, expectedVersion: 1);

        Assert.Equal(2, Scalar<long>("SELECT Version FROM VersionedWidgets WHERE Id = @Id", Key(widget.Id)));
        Assert.Equal("Second", Scalar<string>("SELECT Name FROM VersionedWidgets WHERE Id = @Id", Key(widget.Id)));
    }

    [Fact]
    public async Task UpdateWithVersionThrowsWhenSomeoneElseGotThereFirst()
    {
        var widget = await _versioned.InsertAsync(new VersionedWidget { Name = "First", Version = 1 });
        widget.Name = "Second";
        await _versioned.UpdateWithVersionAsync(widget, expectedVersion: 1);

        // The caller still holds version 1; the row has moved on to 2.
        widget.Name = "Third";
        await Assert.ThrowsAsync<ConcurrencyException>(
            () => _versioned.UpdateWithVersionAsync(widget, expectedVersion: 1));

        Assert.Equal("Second", Scalar<string>("SELECT Name FROM VersionedWidgets WHERE Id = @Id", Key(widget.Id)));
    }
}
