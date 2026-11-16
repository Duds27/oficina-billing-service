using System.Collections.Concurrent;
using System.Globalization;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Oficina.Contracts;
using Testcontainers.RabbitMq;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>PostgreSQL + RabbitMQ (mesma imagem do docker-compose) para a coleção.</summary>
public sealed class MensageriaFixture : IAsyncLifetime
{
    public const string Usuario = "oficina";
    public const string Senha = "oficina_dev";

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
        .WithImage("rabbitmq:4.1-management")
        .WithUsername(Usuario)
        .WithPassword(Senha)
        .Build();

    public PostgresFixture Postgres { get; } = new();

    public string RabbitHost => _rabbit.Hostname;

    public ushort RabbitPort => _rabbit.GetMappedPublicPort(5672);

    public Task InitializeAsync() => Task.WhenAll(_rabbit.StartAsync(), Postgres.InitializeAsync());

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoMensageria : ICollectionFixture<MensageriaFixture>
{
    public const string Nome = "PostgreSQL + RabbitMQ";
}

/// <summary>A API com o RabbitMQ do Testcontainers no lugar do transporte em memória.</summary>
public sealed class RabbitMqApiFactory : ApiFactory
{
    private readonly MensageriaFixture _fixture;

    public RabbitMqApiFactory(MensageriaFixture fixture) : base(fixture.Postgres.NovoBanco()) => _fixture = fixture;

    protected override bool UsarTransporteEmMemoria => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RabbitMq:Host", _fixture.RabbitHost);
        builder.UseSetting("RabbitMq:Port", _fixture.RabbitPort.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("RabbitMq:VirtualHost", "/");
        builder.UseSetting("RabbitMq:Username", MensageriaFixture.Usuario);
        builder.UseSetting("RabbitMq:Password", MensageriaFixture.Senha);
    }
}

/// <summary>
/// Um bus à parte no mesmo RabbitMQ, no papel do os-service: manda os comandos para as filas
/// do contrato e recebe os eventos do billing numa fila própria, como a da saga.
/// </summary>
public sealed class OsServiceSimulado : IAsyncDisposable
{
    private readonly IBusControl _bus;

    private OsServiceSimulado(IBusControl bus) => _bus = bus;

    public ConcurrentQueue<(Guid? MessageId, string[] Tipos, IMensagemOS Mensagem)> Recebidos { get; } = new();

    public static async Task<OsServiceSimulado> IniciarAsync(MensageriaFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        OsServiceSimulado? simulado = null;

        var bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(fixture.RabbitHost, fixture.RabbitPort, "/", h =>
            {
                h.Username(MensageriaFixture.Usuario);
                h.Password(MensageriaFixture.Senha);
            });

            cfg.ReceiveEndpoint($"teste-os-saga-{Guid.NewGuid():N}", e =>
            {
                e.AutoDelete = true;
                e.Handler<OrcamentoGerado>(c => Guardar(simulado!, c));
                e.Handler<OrcamentoAprovado>(c => Guardar(simulado!, c));
                e.Handler<OrcamentoRecusado>(c => Guardar(simulado!, c));
                e.Handler<PagamentoConfirmado>(c => Guardar(simulado!, c));
                e.Handler<PagamentoRecusado>(c => Guardar(simulado!, c));
                e.Handler<PagamentoEstornado>(c => Guardar(simulado!, c));
            });
        });

        simulado = new OsServiceSimulado(bus);
        await bus.StartAsync();
        return simulado;
    }

    public async Task EnviarAsync<T>(string fila, T comando) where T : class
    {
        var endpoint = await _bus.GetSendEndpoint(new Uri($"queue:{fila}"));
        await endpoint.Send(comando);
    }

    public async Task<T> EsperarAsync<T>(Func<T, bool> filtro) where T : class, IMensagemOS
    {
        await Esperar.AteAsync(() => Recebidos.Any(r => r.Mensagem is T m && filtro(m)),
            $"{typeof(T).Name} não chegou ao os-service simulado.", segundos: 30);
        return (T)Recebidos.First(r => r.Mensagem is T m && filtro(m)).Mensagem;
    }

    public async ValueTask DisposeAsync() => await _bus.StopAsync();

    private static Task Guardar<T>(OsServiceSimulado simulado, ConsumeContext<T> contexto) where T : class, IMensagemOS
    {
        simulado.Recebidos.Enqueue((contexto.MessageId, contexto.SupportedMessageTypes.ToArray(), contexto.Message));
        return Task.CompletedTask;
    }
}
