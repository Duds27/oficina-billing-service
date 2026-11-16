using System.Net;
using Oficina.Billing.IntegrationTests.Infraestrutura;

namespace Oficina.Billing.IntegrationTests;

[Collection(ColecaoPostgres.Nome)]
public sealed class HealthChecksTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public HealthChecksTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.NovoBanco());
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Live_responde_200()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Ready_fica_200_com_o_banco_migrado_e_o_bus_no_ar()
    {
        // Readiness de verdade: depois das migrations, o bus ainda leva um instante para subir.
        await Esperar.AteAsync(
            async () => (await _client.GetAsync("/health/ready")).StatusCode == HttpStatusCode.OK ? "pronto" : null,
            _ => true, "O /health/ready não ficou 200.");
    }

    [Fact]
    public async Task Resposta_devolve_o_correlation_id_recebido()
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        requisicao.Headers.Add("X-Correlation-ID", "teste-123");

        var resposta = await _client.SendAsync(requisicao);

        Assert.Equal("teste-123", resposta.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task Ready_responde_503_sem_banco()
    {
        await using var semBanco = new ApiFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2")
        {
            Configuracoes = { ["Database:AplicarMigrations"] = "false" }
        };
        using var client = semBanco.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
