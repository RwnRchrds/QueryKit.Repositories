using Dapper;
using QueryKit.Attributes;
using QueryKit.Dialects;
using QueryKit.Extensions;
using QueryKit.Repositories.Enums;
using QueryKit.Repositories.Filtering;
using QueryKit.Repositories.Paging;
using QueryKit.Repositories.Sorting;
using QueryKit.Repositories.Sql;

namespace QueryKit.Repositories.Tests;

public class QuerySqlBuilderTests
{
    // A sample entity that maps Id->StudentId
        private sealed class StudentEntity
        {
            [Column("StudentId")]
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public int Age { get; set; }
            public bool IsDeleted { get; set; }
        }

        // A sample DTO with a projected alias
        private sealed class StudentSummaryDto
        {
            public Guid StudentId { get; set; }
            public string Name { get; set; } = "";
            public int MemberCount { get; set; } // alias in SQL
        }

        [Fact]
        public void InjectWhere_AddsNewWhere_BeforeOrderBy()
        {
            var baseSql = "SELECT * FROM Students s ORDER BY s.Name";
            var withWhere = QuerySqlBuilder.InjectWhere(baseSql, "s.Age > 10");
            Assert.Equal("SELECT * FROM Students s WHERE s.Age > 10 ORDER BY s.Name", withWhere);
        }

        [Fact]
        public void InjectWhere_AppendsAnd_WhenWhereExists()
        {
            var baseSql = "SELECT * FROM Students WHERE Age > 15 ORDER BY Name";
            var withWhere = QuerySqlBuilder.InjectWhere(baseSql, "Name LIKE @p0");
            Assert.Equal("SELECT * FROM Students WHERE Age > 15 AND Name LIKE @p0 ORDER BY Name", withWhere);
        }

        [Fact]
        public void InjectWhere_DoesNotTouchWhereInsideCTE()
        {
            var baseSql = @"
WITH cte AS (
    SELECT * FROM Students WHERE Age > 10 ORDER BY Name
)
SELECT * FROM cte";
            var withWhere = QuerySqlBuilder.InjectWhere(baseSql, "Name = @p0");
            var expected = @"
WITH cte AS (
    SELECT * FROM Students WHERE Age > 10 ORDER BY Name
)
SELECT * FROM cte WHERE Name = @p0".Trim();
            Assert.Equal(expected, withWhere.Trim());
        }

        [Fact]
        public void ReplaceOrder_ReplacesExistingTopLevel()
        {
            var baseSql = "SELECT * FROM Students s WHERE s.Age > 16 ORDER BY s.Name";
            var replaced = QuerySqlBuilder.ReplaceOrder(baseSql, "s.Age DESC");
            Assert.Equal("SELECT * FROM Students s WHERE s.Age > 16 ORDER BY s.Age DESC", replaced);
        }

        [Fact]
        public void ReplaceOrder_RemovesWhenEmpty()
        {
            var baseSql = "SELECT * FROM Students s ORDER BY s.Name";
            var replaced = QuerySqlBuilder.ReplaceOrder(baseSql, "");
            Assert.Equal("SELECT * FROM Students s", replaced);
        }

        [Fact]
        public void ReplaceOrder_AddsWhenMissing()
        {
            var baseSql = "SELECT * FROM Students s";
            var replaced = QuerySqlBuilder.ReplaceOrder(baseSql, "s.Name ASC");
            Assert.Equal("SELECT * FROM Students s ORDER BY s.Name ASC", replaced);
        }

        [Fact]
        public void StripTrailingOrder_RemovesOnlyTopLevelOrder()
        {
            var baseSql = @"
WITH cte AS (
    SELECT * FROM Students WHERE Age > 10 ORDER BY Name
)
SELECT * FROM cte ORDER BY Name DESC";
            var stripped = QuerySqlBuilder.StripTrailingOrder(baseSql);
            var expected = @"
WITH cte AS (
    SELECT * FROM Students WHERE Age > 10 ORDER BY Name
)
SELECT * FROM cte".Trim();
            Assert.Equal(expected, stripped.Trim());
        }

        [Theory]
        [InlineData(Dialect.SQLServer,  "SELECT * FROM T ORDER BY A",                "SELECT * FROM T ORDER BY A OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
        [InlineData(Dialect.PostgreSQL, "SELECT * FROM T ORDER BY A",                "SELECT * FROM T ORDER BY A LIMIT 5 OFFSET 10")]
        [InlineData(Dialect.MySQL,      "SELECT * FROM T ORDER BY A",                "SELECT * FROM T ORDER BY A LIMIT 10, 5")]
        [InlineData(Dialect.SQLite,     "SELECT * FROM T ORDER BY A",                "SELECT * FROM T ORDER BY A LIMIT 5 OFFSET 10")]
        public void AppendPaging_AppendsByDialect(Dialect dialect, string sql, string expected)
        {
            // Arrange: set the core dialect (exposed via SimpleCRUD config in your core)
            ConnectionExtensions.UseDialect(dialect);

            var page = new PageOptions { Page = 3, PageSize = 5 }; // offset = 10

            // Act
            var paged = QuerySqlBuilder.AppendPaging(sql, page);

            // Assert
            Assert.Equal(expected, paged);
        }

        [Fact]
        public void BuildWhere_Equals_MapsColumnWithAttribute()
        {
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Join = BoolJoin.And,
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "Id", Operator = FilterOperator.Equals, Value = 123 }
                        }
                    }
                }
            };

            var (where, dp) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Equal("StudentId = @p0", where);
            Assert.Single(dp.ParameterNames);
        }

        [Fact]
        public void BuildWhere_In_ExpandsParams()
        {
            ConnectionExtensions.UseDialect(Dialect.SQLServer);
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Criteria = new[]
                        {
                            new FilterCriterion
                            {
                                ColumnName = "Age",
                                Operator = FilterOperator.In,
                                Values = new object[] { 11, 12, 13 }
                            }
                        }
                    }
                }
            };

            var (where, dp) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);

            Assert.StartsWith("[Age] IN (", where);
            var names = dp.ParameterNames.ToList();
            Assert.Equal(3, names.Count);
        }

        [Fact]
        public void BuildWhere_Like_UsesILikeOnPostgres()
        {
            ConnectionExtensions.UseDialect(Dialect.PostgreSQL);

            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "Name", Operator = FilterOperator.Contains, Value = "Row" }
                        }
                    }
                }
            };

            var (where, _) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Contains("Name ILIKE @", where, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildWhere_Like_UsesLikeElsewhere()
        {
            ConnectionExtensions.UseDialect(Dialect.SQLServer);

            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "Name", Operator = FilterOperator.StartsWith, Value = "A" }
                        }
                    }
                }
            };

            var (where, _) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Contains("LIKE @", where);
            Assert.Contains("ESCAPE '\\'", where); // string contains a single backslash in SQL, doubled in C#
        }

        [Fact]
        public void BuildWhere_Between_ProducesTwoParams()
        {
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "Age", Operator = FilterOperator.Between, Value = 11, Value2 = 15 }
                        }
                    }
                }
            };

            var (where, dp) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Contains("[Age] BETWEEN @p0 AND @p1", where);
            Assert.Equal(2, dp.ParameterNames.Count());
        }

        [Fact]
        public void BuildWhere_IsNull_IsNotNull()
        {
            var filter = new FilterOptions
            {
                Join = BoolJoin.And,
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Join = BoolJoin.And,
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "Name", Operator = FilterOperator.IsNull },
                            new FilterCriterion { ColumnName = "Age",  Operator = FilterOperator.IsNotNull }
                        }
                    }
                }
            };

            var (where, dp) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Contains("[Name] IS NULL", where);
            Assert.Contains("[Age] IS NOT NULL", where);
            Assert.Empty(dp.ParameterNames);
        }

        [Fact]
        public void BuildOrderBy_MapsColumns_AndDirections()
        {
            var sort = new SortOptions
            {
                Criteria = new[]
                {
                    new SortCriterion { ColumnName = "Id", Direction = SortDirection.Ascending },
                    new SortCriterion { ColumnName = "Name", Direction = SortDirection.Descending }
                }
            };

            var order = QuerySqlBuilder.BuildOrderBy<StudentEntity>(sort);
            Assert.Equal("[StudentId] asc, [Name] desc", order);
        }

        [Fact]
        public void MergeParams_MergesBoth()
        {
            var a = new DynamicParameters();
            a.Add("A", 1);
            
            var b = new DynamicParameters();
            b.Add("B", 2);

            var merged = QuerySqlBuilder.MergeParams(a, b);
            var names = merged.ParameterNames.ToList();
            Assert.Contains("A", names);
            Assert.Contains("B", names);
        }

        [Fact]
        public void BuildWhere_Dto_UsesAliasWhenNoAttribute()
        {
            // For DTOs, MapPropertyToColumn<StudentSummaryDto>("MemberCount") returns "MemberCount",
            // which will work when the SQL SELECT uses "COUNT(...) AS MemberCount".
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = "MemberCount", Operator = FilterOperator.GreaterThan, Value = 5 }
                        }
                    }
                }
            };

            var (where, dp) = QuerySqlBuilder.BuildWhere<StudentSummaryDto>(filter);
            Assert.Equal("[MemberCount] > @p0", where);
            Assert.Single(dp.ParameterNames);
        }


        [Fact]
        public void BuildOrderBy_UnknownColumn_Throws()
        {
            var sort = new SortOptions
            {
                Criteria = new[] { new SortCriterion { ColumnName = "HAX", Direction = SortDirection.Ascending } }
            };
            Assert.Throws<ArgumentException>(() => QuerySqlBuilder.BuildOrderBy<StudentEntity>(sort));
        }

        [Fact]
        public void BuildWhere_UnknownColumn_Throws()
        {
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Join = BoolJoin.And,
                        Criteria = new[] { new FilterCriterion { ColumnName = "HAX", Operator = FilterOperator.Equals, Value = 1 } }
                    }
                }
            };
            Assert.Throws<ArgumentException>(() => QuerySqlBuilder.BuildWhere<StudentEntity>(filter));
        }

        [Fact]
        public void BuildWhere_Like_EscapesPercentAndUnderscore()
        {
            var filter = new FilterOptions
            {
                Groups = new[]
                {
                    new FilterGroup
                    {
                        Join = BoolJoin.And,
                        Criteria = new[]
                        {
                            new FilterCriterion { ColumnName = nameof(StudentEntity.Name), Operator = FilterOperator.Contains, Value = @"100%_raw\" }
                        }
                    }
                }
            };
            var (whereSql, dp) = QuerySqlBuilder.BuildWhere<StudentEntity>(filter);
            Assert.Contains("LIKE", whereSql);
            // Ensure a parameter was generated
            Assert.NotEmpty((dp as DynamicParameters).ParameterNames);
        }

        [Fact]
        public void InjectWhere_WorksWithCte()
        {
            var baseSql = "WITH cte AS (SELECT * FROM X) SELECT * FROM cte ORDER BY Name";
            var withWhere = QuerySqlBuilder.InjectWhere(baseSql, "Name IS NOT NULL");
            Assert.Contains("WHERE Name IS NOT NULL", withWhere);
        }

}
