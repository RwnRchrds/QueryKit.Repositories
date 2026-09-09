# QueryKit.Repositories

Repository base classes and query helpers (filter / sort / page) for [BlockSoftware.QueryKit](https://www.nuget.org/packages/BlockSoftware.QueryKit).

Built on top of Dapper. Provides a small, opinionated `BaseEntityRepository<TEntity, TKey>` with built-in support for:

- Strongly-typed CRUD over Dapper, including batched multi-row inserts
- Composable, type-safe filtering, sorting, and paging
- Soft delete via attribute
- Optimistic concurrency
- Optional `IDbTransaction` on every method
- Multi-dialect SQL generation (SQL Server, PostgreSQL, MySQL, SQLite)
- Hooks for custom SQL and projected DTOs

## Install

```bash
dotnet add package BlockSoftware.QueryKit.Repositories
```

> Previously published as `Rowan.QueryKit.Repositories`. The package ID changed at 0.10.0; the
> namespaces, types and API are unchanged, so switching is a one-line edit to your
> `PackageReference`.

## Configure the dialect

Set the SQL dialect once at startup. The dialect drives identifier escaping, paging syntax, and `LIKE`/`ILIKE` selection.

```csharp
using QueryKit.Dialects;
using QueryKit.Extensions;

ConnectionExtensions.UseDialect(Dialect.SQLServer); // or PostgreSQL, MySQL, SQLite
```

## Concepts

| Type | Purpose |
| --- | --- |
| `IBaseEntity<TKey>` | Marker interface for entities with a primary key. |
| `IConnectionFactory` | Creates `IDbConnection` instances per operation. |
| `BaseEntityReadRepository<TEntity, TKey>` | Read-only repository: `GetByIdAsync`, list, paged list, uniqueness checks. |
| `BaseEntityRepository<TEntity, TKey>` | Adds insert / update / delete / upsert / undelete. |
| `FilterOptions`, `SortOptions`, `PageOptions` | Composable query inputs. |
| `[SoftDelete]` | Marks the boolean property used for soft deletes. |

## Quick start

### 1. Define an entity

Map columns with `[Table]` / `[Column]` (from `QueryKit.Attributes`) where the CLR name doesn't match the database name. Mark a boolean as soft-delete with `[SoftDelete]`.

```csharp
using QueryKit.Attributes;
using QueryKit.Repositories.Attributes;
using QueryKit.Repositories.Interfaces;

[Table("Students")]
public class Student : IBaseEntity<Guid>
{
    [Column("StudentId")]
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
    public int Age { get; set; }

    [SoftDelete]
    public bool IsDeleted { get; set; }
}
```

### 2. Provide a connection factory

```csharp
using System.Data;
using Microsoft.Data.SqlClient;
using QueryKit.Repositories.Interfaces;

public sealed class SqlServerConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;
    public SqlServerConnectionFactory(string connectionString) => _connectionString = connectionString;
    public IDbConnection Create() => new SqlConnection(_connectionString);
}
```

### 3. Define a repository

The minimal repository is just a derived class — no body required.

```csharp
using QueryKit.Repositories;
using QueryKit.Repositories.Interfaces;

public class StudentRepository : BaseEntityRepository<Student, Guid>
{
    public StudentRepository(IConnectionFactory factory) : base(factory) { }
}
```

### 4. Use it

```csharp
var factory = new SqlServerConnectionFactory("Server=...;Database=...;");
var students = new StudentRepository(factory);

var alice = await students.InsertAsync(new Student { Name = "Alice", Age = 21 });
alice.Age = 22;
await students.UpdateAsync(alice);

var fetched = await students.GetByIdAsync(alice.Id);
await students.DeleteAsync(alice.Id); // soft-deletes by default
```

## Filtering

`FilterOptions` is a list of `FilterGroup`s; each group is a list of `FilterCriterion`s. Each level has its own `BoolJoin` (`And` / `Or`).

```csharp
using QueryKit.Repositories.Enums;
using QueryKit.Repositories.Filtering;

var filter = new FilterOptions
{
    Join = BoolJoin.And, // joins between groups
    Groups = new[]
    {
        new FilterGroup
        {
            Join = BoolJoin.And, // joins between criteria within this group
            Criteria = new[]
            {
                new FilterCriterion { ColumnName = nameof(Student.Age), Operator = FilterOperator.GreaterThanOrEqual, Value = 18 },
                new FilterCriterion { ColumnName = nameof(Student.Age), Operator = FilterOperator.LessThan,           Value = 30 },
            }
        },
        new FilterGroup
        {
            Join = BoolJoin.Or,
            Criteria = new[]
            {
                new FilterCriterion { ColumnName = nameof(Student.Name), Operator = FilterOperator.StartsWith, Value = "A" },
                new FilterCriterion { ColumnName = nameof(Student.Name), Operator = FilterOperator.EndsWith,   Value = "z" },
            }
        }
    }
};

var page = await students.GetListPagedAsync(filter);
```

For the common single-group case use the helper:

```csharp
var filter = FilterOptions.From(
    new FilterCriterion { ColumnName = "Name", Operator = FilterOperator.Contains, Value = "Smith" });
```

### Supported operators

| Operator | Notes |
| --- | --- |
| `Equals` / `NotEquals` | `IS NULL` / `IS NOT NULL` automatically when value is `null` or `DBNull`. |
| `Contains` / `NotContains` | `LIKE '%v%'`; `ILIKE` on PostgreSQL. `[`, `%`, `_`, `\` are escaped on SQL Server. |
| `StartsWith` / `EndsWith` | `LIKE 'v%'` / `LIKE '%v'`. |
| `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual` | Standard comparisons. |
| `In` | Pass `Values = new object[] { … }`. Empty array yields `1 = 0`. |
| `Between` | Pass `Value` and `Value2`. |
| `IsNull` / `IsNotNull` | Value-less. |

> Only mapped CLR properties are accepted for `ColumnName`. Unknown names throw `ArgumentException` — no silent fall-through.

## Sorting

```csharp
using QueryKit.Repositories.Enums;
using QueryKit.Repositories.Sorting;

var sort = new SortOptions
{
    Criteria = new[]
    {
        new SortCriterion { ColumnName = "Age",  Direction = SortDirection.Descending },
        new SortCriterion { ColumnName = "Name", Direction = SortDirection.Ascending  },
    }
};

// Or a single-criterion shortcut:
var byName = SortOptions.By("Name", SortDirection.Ascending);
```

If no sort is supplied to `GetListPagedAsync`, the repository orders by the entity's primary key ascending so paging is deterministic.

## Paging

```csharp
using QueryKit.Repositories.Paging;

var paging = new PageOptions { Page = 2, PageSize = 25 };
// Or: PageOptions.Create(2, 25)

PageResult<Student> result = await students.GetListPagedAsync(
    filter: filter,
    sort:   sort,
    paging: paging);

foreach (var s in result.Items) { /* … */ }
int total = result.TotalItems;
```

`PageOptions` clamps `Page` to `>= 1` and `PageSize` to `[1, MaxPageSize=500]`. Defaults: page 1, page size 50.


## Bulk inserts

`InsertAsync` costs one round trip per entity. Where the whole collection is known up front,
`BatchInsertAsync` sends many rows per statement and returns the number of rows written:

```csharp
var intake = names.Select(n => new Student { Name = n }).ToArray();

int written = await students.BatchInsertAsync(intake, ct);
```

It takes an optional `IDbTransaction` and `batchSize` like the other methods:

```csharp
await students.BatchInsertAsync(intake, ct, tx, batchSize: 200);
```

Entities keep the keys they are given, and empty `Guid` keys are filled in per row. Identity keys
are not supported — there is no portable way to read many generated keys back from one statement —
so insert those individually.
## Soft delete

Mark a boolean property with `[SoftDelete]` and `DeleteAsync` flips it to `true` instead of issuing `DELETE`. Reads exclude soft-deleted rows by default.

```csharp
[SoftDelete] public bool IsDeleted { get; set; }
```

```csharp
await students.DeleteAsync(id);                    // soft-deletes
await students.DeleteAsync(id, softDelete: false); // hard delete
await students.UndeleteAsync(id);                  // sets IsDeleted = false

// Include soft-deleted rows in a query:
var all = await students.GetListAsync(includeDeleted: true);
```

If your custom SQL aliases the entity table (e.g. `FROM Students s`), override `DefaultAlias` so the soft-delete predicate is qualified:

```csharp
public class StudentRepository : BaseEntityRepository<Student, Guid>
{
    public StudentRepository(IConnectionFactory factory) : base(factory) { }
    protected override string? DefaultAlias => "s";
}
```

## Uniqueness checks

```csharp
bool emailFree = await students.IsUniqueExcludingDeletedAsync(nameof(Student.Email), "a@b.com");
bool everUsed  = await students.IsUniqueIncludingDeletedAsync(nameof(Student.Email), "a@b.com");
```

Pass `null` to check for `IS NULL`.

## Optimistic concurrency

For entities versioned by QueryKit's row-version mechanism, use `UpdateWithVersionAsync`. It throws `ConcurrencyException` if the row was modified or removed since you read it.

```csharp
try
{
    await students.UpdateWithVersionAsync(student, expectedVersion: student.RowVersion);
}
catch (ConcurrencyException)
{
    // Reload, merge, retry, or surface to the user.
}
```

## Transactions

Every public method accepts an optional `IDbTransaction` as its **last** parameter. When supplied, the repository borrows the transaction's connection and does **not** open or dispose its own.

```csharp
using var conn = factory.Create();
conn.Open();
using var tx = conn.BeginTransaction();

try
{
    await students.InsertAsync(alice, ct, tx);
    await classes.InsertAsync(maths, ct, tx);
    await enrolments.InsertAsync(new Enrolment(alice.Id, maths.Id), ct, tx);

    tx.Commit();
}
catch
{
    tx.Rollback();
    throw;
}
```

> The parameter ordering puts `transaction` after `cancellationToken` so existing positional callers keep working — pass it by name (`transaction: tx`) for clarity.

## Custom SQL

Derived repositories have access to protected helpers for one-off queries against the entity type or any DTO. All overloads accept optional `IDbTransaction` and `CancellationToken`.

### Single record / list

```csharp
public class StudentRepository : BaseEntityRepository<Student, Guid>
{
    public StudentRepository(IConnectionFactory factory) : base(factory) { }

    public Task<Student?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        GetAsync(
            "SELECT * FROM Students WHERE Email = @Email",
            new { Email = email },
            ct);

    public Task<IList<Student>> GetByCohortAsync(int year, CancellationToken ct = default) =>
        GetListAsync(
            "SELECT * FROM Students WHERE CohortYear = @Year",
            new { Year = year },
            ct);
}
```

### Projected DTOs

```csharp
public sealed class StudentSummary
{
    public Guid StudentId { get; set; }
    public string Name { get; set; } = "";
    public int EnrolmentCount { get; set; }
}

public Task<IList<StudentSummary>> GetSummariesAsync(CancellationToken ct = default) =>
    GetListAsync<StudentSummary>(@"
        SELECT s.StudentId, s.Name, COUNT(e.Id) AS EnrolmentCount
        FROM Students s
        LEFT JOIN Enrolments e ON e.StudentId = s.StudentId
        GROUP BY s.StudentId, s.Name", parameters: null, ct);
```

### Paging custom SQL with `FilterOptions` / `SortOptions`

The repository safely injects your `WHERE` and `ORDER BY` into top-level clauses (CTEs and subqueries are left alone) and applies dialect-aware paging:

```csharp
public Task<PageResult<StudentSummary>> GetSummariesPagedAsync(
    FilterOptions? filter,
    SortOptions? sort,
    PageOptions paging,
    CancellationToken ct = default)
{
    const string sql = @"
        SELECT s.StudentId, s.Name, COUNT(e.Id) AS EnrolmentCount
        FROM Students s
        LEFT JOIN Enrolments e ON e.StudentId = s.StudentId
        GROUP BY s.StudentId, s.Name";

    return GetListPagedAsync<StudentSummary>(
        sql, parameters: null,
        filter, sort, paging,
        cancellationToken: ct);
}
```

If you'd rather hand-write both the data and count SQL:

```csharp
return GetListPagedAsync<StudentSummary>(
    dataSql:  "SELECT … FROM … ORDER BY …",
    countSql: "SELECT COUNT(*) FROM …",
    parameters: new { CohortYear = year },
    paging: paging,
    cancellationToken: ct);
```

## Multi-dialect support

| Dialect | Identifier escape | Paging | `LIKE` |
| --- | --- | --- | --- |
| SQL Server | `[name]` | `OFFSET … ROWS FETCH NEXT …` (requires `ORDER BY`) | `LIKE … ESCAPE '\'`, brackets escaped |
| PostgreSQL | `"name"` | `LIMIT … OFFSET …` | `ILIKE` (case-insensitive) |
| MySQL | `` `name` `` | `LIMIT offset, size` | `LIKE … ESCAPE '\'` |
| SQLite | `"name"` | `LIMIT … OFFSET …` | `LIKE … ESCAPE '\'` |

Switch at startup; column-name caching is keyed per-dialect so you can use multiple dialects in tests.

## Extending

`OpenConnection`, `AcquireConnection`, and the protected `GetAsync` / `GetListAsync` / `GetListPagedAsync` overloads are all `virtual`/`protected` — override them to add logging, retries, multi-tenant connection routing, or to take part in a higher-level unit of work.

`ConnectionLease` is the helper used internally to keep transaction-borrowed connections alive while disposing repository-opened ones; you can use it directly from a derived class:

```csharp
public async Task<int> CountActiveAsync(IDbTransaction? tx = null, CancellationToken ct = default)
{
    using var lease = await AcquireConnection(tx, ct);
    return await lease.Connection.ExecuteScalarAsync<int>(
        new CommandDefinition("SELECT COUNT(*) FROM Students WHERE IsDeleted = 0", transaction: tx, cancellationToken: ct));
}
```

## License

MIT — see [LICENSE](LICENSE).
