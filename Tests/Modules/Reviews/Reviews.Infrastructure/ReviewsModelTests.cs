using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Reviews.Domain;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure;

/// <summary>Les règles du mapping EF Core, vérifiées sur le MODÈLE (aucune base, aucun Docker).</summary>
public sealed class ReviewsModelTests : IDisposable
{
    private static readonly string[] ModuleTables = ["feedbacks", "surveys", "survey_questions", "survey_responses"];

    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly ReviewsDbContext _context;

    public ReviewsModelTests()
    {
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(new FakeClock())
            .AddReviewsInfrastructure("Host=model-only;Database=reviews_model")
            .BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
        _context = _scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
    }

    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    private IEnumerable<ITable> Tables => Model.GetRelationalModel().Tables.Where(table => ModuleTables.Contains(table.Name));

    public void Dispose()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void The_migration_matches_the_model()
    {
        // Échoue tant que la migration n'a pas été (re)générée après un changement de configuration :
        // dotnet ef migrations add InitialCreate --project src/Modules/Reviews/Reviews.Infrastructure --output-dir Persistence/Migrations
        Assert.False(_context.Database.HasPendingModelChanges(), "Le modèle a changé : régénérer la migration Reviews.");
    }

    [Fact]
    public void Every_table_lives_in_the_reviews_schema_with_snake_case_and_bounded_text()
    {
        const string snakeCase = "^[a-z][a-z0-9_]*$";
        Assert.Equal(ModuleTables.Order(), Tables.Select(table => table.Name).Order());
        Assert.All(Tables, table =>
        {
            Assert.Equal(DependencyInjection.Schema, table.Schema);
            Assert.All(table.Columns, column => Assert.Matches(snakeCase, column.Name));
            Assert.All(table.Columns, column => Assert.NotEqual("text", column.StoreType));
            Assert.All(table.Indexes, index => Assert.StartsWith("ix_" + table.Name, index.Name, StringComparison.Ordinal));
            Assert.All(table.CheckConstraints, check => Assert.StartsWith("ck_" + table.Name + "_", check.Name, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void One_feedback_per_order_is_enforced_by_a_partial_unique_index()
    {
        var table = Assert.Single(Tables, candidate => candidate.Name == "feedbacks");
        var index = Assert.Single(table.Indexes, candidate => candidate.Name == FeedbackConfiguration.OrderIndex);

        Assert.True(index.IsUnique);
        Assert.Equal(["tenant_id", "order_id"], index.Columns.Select(column => column.Name));
        Assert.Equal("order_id IS NOT NULL", index.Filter);
        Assert.Equal("integer", table.FindColumn("rating")!.StoreType);
        Assert.Equal("jsonb", Tables.Single(candidate => candidate.Name == "survey_questions").FindColumn("choices")!.StoreType);
    }

    [Fact]
    public void Feedback_and_survey_carry_an_optimistic_version_and_questions_are_deleted_with_their_survey()
    {
        Assert.Contains(Model.FindEntityType(typeof(Feedback))!.GetProperties(), property => property.IsConcurrencyToken);
        Assert.Contains(Model.FindEntityType(typeof(Survey))!.GetProperties(), property => property.IsConcurrencyToken);
        var owner = Assert.Single(Model.FindEntityType(typeof(SurveyQuestion))!.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Cascade, owner.DeleteBehavior);
        Assert.True(owner.IsRequired);
    }
}
