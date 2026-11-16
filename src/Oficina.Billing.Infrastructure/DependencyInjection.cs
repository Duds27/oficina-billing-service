using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Application;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Infrastructure.Messaging;
using Oficina.Billing.Infrastructure.Notificacoes;
using Oficina.Billing.Infrastructure.Pagamentos;
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

        services.Configure<OrcamentoOptions>(configuration.GetSection(OrcamentoOptions.Secao));
        services.Configure<PagamentosOptions>(configuration.GetSection(PagamentosOptions.Secao));

        // A persistência primeiro: as migrations rodam antes de o bus começar a consumir.
        services.AddPersistencia(configuration);
        services.AddGatewayPagamento(configuration);
        services.AddMensageria(configuration);

        services.Configure<NotificacoesOptions>(configuration.GetSection(NotificacoesOptions.Secao));
        services.AddHostedService<EnvioNotificacoes>();
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

    /// <summary><c>Pagamentos__Gateway</c>: <c>Fake</c> (padrão) ou <c>MercadoPago</c>.</summary>
    private static IServiceCollection AddGatewayPagamento(this IServiceCollection services, IConfiguration configuration)
    {
        var gateway = configuration.GetSection(PagamentosOptions.Secao).Get<PagamentosOptions>()?.Gateway ?? "Fake";

        // O webhook valida a assinatura com o segredo do Mercado Pago, qualquer que seja o gateway:
        // sem segredo configurado, recusa tudo.
        services.Configure<MercadoPagoOptions>(configuration.GetSection(MercadoPagoOptions.Secao));
        services.AddSingleton<IValidadorWebhook, AssinaturaWebhook>();

        switch (gateway)
        {
            case "Fake":
                services.AddSingleton<FakeGatewayPagamento>();
                services.AddSingleton<IGatewayPagamento>(sp => sp.GetRequiredService<FakeGatewayPagamento>());
                services.AddSingleton<ISimuladorPagamentos>(sp => sp.GetRequiredService<FakeGatewayPagamento>());
                break;
            case "MercadoPago":
                var timeout = configuration.GetSection(MercadoPagoOptions.Secao).Get<MercadoPagoOptions>()?.TimeoutPorTentativa
                    ?? TimeSpan.FromSeconds(5);
                services.AddHttpClient<IGatewayPagamento, MercadoPagoGateway>()
                    .AddStandardResilienceHandler(o =>
                    {
                        // Timeout por tentativa, retry com backoff exponencial em erro transitório
                        // (5xx, 408, 429, falha de rede) e circuit breaker. Seguro porque as
                        // chamadas que criam algo levam X-Idempotency-Key.
                        o.AttemptTimeout.Timeout = timeout;
                        o.Retry.MaxRetryAttempts = 3;
                        o.Retry.Delay = TimeSpan.FromMilliseconds(300);
                        o.TotalRequestTimeout.Timeout = timeout * 5;
                        o.CircuitBreaker.SamplingDuration = timeout * 2 + TimeSpan.FromSeconds(1);
                    });
                break;
            default:
                throw new InvalidOperationException($"Pagamentos:Gateway inválido: {gateway}. Use Fake ou MercadoPago.");
        }

        return services;
    }
}
