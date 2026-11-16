using MassTransit;
using MassTransit.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oficina.Billing.Infrastructure.Messaging;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>
/// A API num banco PostgreSQL só dela; as migrations rodam na inicialização. Por padrão, o
/// RabbitMQ é trocado pelo transporte em memória do test harness, com o mesmo outbox/inbox no banco.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveJwt = "OficinaMecanicaSecretKeyMustBe32CharactersMin!";

    public ApiFactory(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    /// <summary>Configurações extras do teste (chave no formato <c>Secao:Chave</c>).</summary>
    public Dictionary<string, string> Configuracoes { get; } = [];

    /// <summary>Falso nos testes com RabbitMQ de verdade.</summary>
    protected virtual bool UsarTransporteEmMemoria => true;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Key", ChaveJwt);
        builder.UseSetting("ConnectionStrings:BillingDb", ConnectionString);
        builder.UseSetting("Notificacoes:IntervaloMs", "100");
        foreach (var (chave, valor) in Configuracoes)
            builder.UseSetting(chave, valor);

        builder.ConfigureTestServices(services =>
        {
            if (!UsarTransporteEmMemoria)
                return;

            // O callback de endpoint (retry + outbox) registrado pelo Program sobrevive à troca
            // do bus pelo harness. Sem remover, o ConfigurarBus abaixo registraria outro, e cada
            // fila teria dois outboxes: transação dentro de transação.
            services.RemoveAll<Bind<IBus, IConfigureReceiveEndpoint>>();
            services.AddMassTransitTestHarness(x =>
            {
                MensageriaExtensions.ConfigurarBus(x);
                x.AddConsumer<CapturaDeEventos>().Endpoint(e => e.Name = "teste-captura-eventos");
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseConsumeFilter(typeof(EscopoDeLogFilter<>), context);
                    cfg.ConfigureEndpoints(context);
                });
            });
        });
    }
}
