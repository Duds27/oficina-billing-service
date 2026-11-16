using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Domain;
using Oficina.Billing.Infrastructure.Persistence;
using Oficina.Contracts;

namespace Oficina.Billing.Infrastructure.Messaging;

public static class MensageriaExtensions
{
    /// <summary>
    /// MassTransit 8.4.1 + RabbitMQ, com outbox e inbox no PostgreSQL (BillingDbContext), no
    /// mesmo padrão do os-service (ADR-007).
    /// </summary>
    public static IServiceCollection AddMensageria(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPublicadorEventos, MassTransitPublicadorEventos>();

        services.AddMassTransit(x =>
        {
            ConfigurarBus(x);

            x.ConfigureHealthCheckOptions(o =>
            {
                o.Name = "masstransit";
                o.MinimalFailureStatus = HealthStatus.Unhealthy;
                o.Tags.Add("ready");
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var rabbit = configuration.GetSection(RabbitMqOptions.Secao).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
                cfg.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, h =>
                {
                    h.Username(rabbit.Username);
                    h.Password(rabbit.Password);
                });

                cfg.UseConsumeFilter(typeof(EscopoDeLogFilter<>), context);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    /// <summary>
    /// Consumidores, outbox/inbox e retry. Público para os testes montarem o mesmo bus sobre o
    /// transporte em memória do test harness.
    /// </summary>
    public static void ConfigurarBus(IBusRegistrationConfigurator x)
    {
        ArgumentNullException.ThrowIfNull(x);

        x.SetKebabCaseEndpointNameFormatter();

        x.AddConsumer<GerarOrcamentoConsumer>().Endpoint(e => e.Name = FilasBilling.GerarOrcamento);
        x.AddConsumer<CancelarOrcamentoConsumer>().Endpoint(e => e.Name = FilasBilling.CancelarOrcamento);
        x.AddConsumer<EstornarPagamentoConsumer>().Endpoint(e => e.Name = FilasBilling.EstornarPagamento);
        x.AddConsumer<ProcessarNotificacaoPagamentoConsumer>().Endpoint(e => e.Name = FilasBilling.ProcessarNotificacaoPagamento);
        x.AddConsumer<FalhaDeEstornoConsumer>().Endpoint(e => e.Name = FilasBilling.FalhasDeEstorno);

        // Publicações fora de um consumidor (a aprovação e o webhook, na API) vão para a tabela
        // OutboxMessage no mesmo SaveChanges e saem depois do commit.
        x.AddEntityFrameworkOutbox<BillingDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
        });

        x.AddConfigureEndpointsCallback((context, _, endpoint) =>
        {
            // Falha transitória (broker, banco, gateway, concorrência): novas tentativas com
            // intervalo crescente. Esgotou, a mensagem vai para <fila>_error. O redelivery
            // atrasado precisa do plugin de delayed exchange e fica para a Etapa 4.
            endpoint.UseMessageRetry(r =>
            {
                r.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
                // Erro de regra ou de dado inválido não se resolve tentando de novo.
                r.Ignore<RegraDeNegocioException>();
                r.Ignore<ArgumentException>();
                r.Ignore<GatewayRecusouException>();
            });

            // Inbox (descarta a mensagem repetida pelo MessageId) + outbox das mensagens
            // publicadas pelo consumidor, na mesma transação das mudanças no banco.
            endpoint.UseEntityFrameworkOutbox<BillingDbContext>(context);
        });
    }
}
