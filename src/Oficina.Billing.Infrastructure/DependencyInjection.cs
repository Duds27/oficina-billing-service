using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Infrastructure.Persistence;

namespace Oficina.Billing.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "BillingDb";

    /// <summary>
    /// Registra persistência, mensageria e integrações externas.
    /// Cada dependência adiciona aqui o seu health check com a tag "ready",
    /// que passa a compor o <c>/health/ready</c>.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistencia(configuration);
        return services;
    }

    private static IServiceCollection AddPersistencia(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} não configurada.");

        services.AddDbContext<BillingDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IBillingRepositorio, BillingRepositorio>();

        // Antes de qualquer hosted service que use o banco.
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Secao));
        services.AddHostedService<MigradorBanco>();

        services.AddHealthChecks().AddDbContextCheck<BillingDbContext>("postgres", tags: ["ready"]);
        return services;
    }
}
