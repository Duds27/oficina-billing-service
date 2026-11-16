using Microsoft.EntityFrameworkCore;
using Npgsql;
using Oficina.Billing.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>
/// Um PostgreSQL 16 (mesma imagem do docker-compose) para a coleção inteira. Cada teste usa
/// um banco próprio, criado pelas migrations, para não depender dos dados dos outros.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Connection string de um banco novo (as migrations criam o banco).</summary>
    public string NovoBanco() => new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
    {
        Database = $"billing_{Guid.NewGuid():N}"
    }.ConnectionString;

    public async Task<BillingDbContext> NovoContextoMigradoAsync(string? connectionString = null)
    {
        var db = Contexto(connectionString ?? NovoBanco());
        await db.Database.MigrateAsync();
        return db;
    }

    public static BillingDbContext Contexto(string connectionString) =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connectionString).Options);
}

[CollectionDefinition(Nome)]
public sealed class ColecaoPostgres : ICollectionFixture<PostgresFixture>
{
    public const string Nome = "PostgreSQL";
}
