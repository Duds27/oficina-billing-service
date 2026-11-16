using System.Net;
using System.Security.Cryptography;
using System.Text;
using MassTransit.Testing;
using Oficina.Billing.IntegrationTests.Infraestrutura;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Api;

[Collection(ColecaoPostgres.Nome)]
public sealed class WebhookApiTests : IAsyncLifetime
{
    private const string Segredo = "segredo-de-teste-do-webhook";

    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public WebhookApiTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.NovoBanco()) { Configuracoes = { ["Pagamentos:MercadoPago:WebhookSecret"] = Segredo } };
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    /// <summary>Notificação como o Mercado Pago manda: query <c>data.id</c> e <c>type</c>, headers assinados.</summary>
    internal static HttpRequestMessage Notificacao(string dataId, string type = "payment", string segredo = Segredo, string? requestId = null)
    {
        requestId ??= Guid.NewGuid().ToString();
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var manifesto = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        var v1 = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto))).ToLowerInvariant();

        var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/mercadopago?data.id={dataId}&type={type}")
        {
            Content = new StringContent($$$"""{"action":"payment.updated","type":"{{{type}}}","data":{"id":"{{{dataId}}}"}}""", Encoding.UTF8, "application/json")
        };
        requisicao.Headers.Add("x-signature", $"ts={ts},v1={v1}");
        requisicao.Headers.Add("x-request-id", requestId);
        return requisicao;
    }

    [Fact]
    public async Task Notificacao_assinada_responde_200_e_vai_para_o_processamento()
    {
        using var requisicao = Notificacao("1234567890", requestId: "req-1");

        var resposta = await _client.SendAsync(requisicao);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var harness = _factory.Services.GetTestHarness();
        await Esperar.AteAsync(
            () => harness.Consumed.Select<ProcessarNotificacaoPagamento>(m => m.Context.Message.MpPaymentId == "1234567890" && m.Context.Message.RequestId == "req-1").Any(),
            "A notificação não foi processada.");
    }

    [Fact]
    public async Task Assinatura_invalida_responde_401()
    {
        using var outroSegredo = Notificacao("1234567890", segredo: "segredo-errado");
        using var semAssinatura = new HttpRequestMessage(HttpMethod.Post, "/webhooks/mercadopago?data.id=1&type=payment");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(outroSegredo)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendAsync(semAssinatura)).StatusCode);
    }

    [Fact]
    public async Task Outro_topico_responde_200_sem_processar()
    {
        using var requisicao = Notificacao("555", type: "merchant_order");

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(requisicao)).StatusCode);
        await Task.Delay(500);
        Assert.False(_factory.Services.GetTestHarness().Consumed.Select<ProcessarNotificacaoPagamento>(m => m.Context.Message.MpPaymentId == "555").Any());
    }

    [Fact]
    public async Task Sem_segredo_configurado_recusa_tudo()
    {
        await using var semSegredo = new ApiFactory(_postgres.NovoBanco());
        using var client = semSegredo.CreateClient();
        using var requisicao = Notificacao("1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(requisicao)).StatusCode);
    }
}
