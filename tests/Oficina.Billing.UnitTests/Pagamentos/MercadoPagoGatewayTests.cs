using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Infrastructure.Pagamentos;

namespace Oficina.Billing.UnitTests.Pagamentos;

/// <summary>Formato das chamadas e tradução das respostas, com um handler HTTP falso.</summary>
public class MercadoPagoGatewayTests
{
    private static readonly Guid PagamentoId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000002");

    private readonly HandlerFalso _handler = new();

    private MercadoPagoGateway Gateway(bool sandbox = true) => new(
        new HttpClient(_handler),
        Options.Create(new MercadoPagoOptions
        {
            BaseUrl = new Uri("https://mp.teste/"),
            AccessToken = "TEST-token-falso",
            NotificationUrl = new Uri("https://api.oficina/webhooks/mercadopago"),
            UsarLinkSandbox = sandbox
        }));

    [Fact]
    public async Task Cria_a_preferencia_com_um_item_pelo_total_e_idempotencia_pelo_pagamento()
    {
        _handler.Responder(HttpStatusCode.Created, """{"id":"123-abc","init_point":"https://mp/init","sandbox_init_point":"https://mp/sandbox"}""");

        var cobranca = await Gateway().CriarCobrancaAsync(
            new NovaCobranca(PagamentoId, "OS-0042", 329.80m, "maria@example.com", new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new CobrancaCriada("123-abc", "https://mp/sandbox"), cobranca);
        var req = Assert.Single(_handler.Requisicoes);
        Assert.Equal(HttpMethod.Post, req.Metodo);
        Assert.Equal("https://mp.teste/checkout/preferences", req.Url);
        Assert.Equal("Bearer TEST-token-falso", req.Authorization);
        Assert.Equal(PagamentoId.ToString(), req.Idempotencia);

        var corpo = JsonNode.Parse(req.Corpo!)!;
        var item = Assert.Single(corpo["items"]!.AsArray())!;
        Assert.Equal(1, item["quantity"]!.GetValue<int>());
        Assert.Equal(329.80m, item["unit_price"]!.GetValue<decimal>());
        Assert.Equal("BRL", item["currency_id"]!.GetValue<string>());
        Assert.Equal("Orçamento da OS OS-0042", item["title"]!.GetValue<string>());
        Assert.Equal(PagamentoId.ToString(), corpo["external_reference"]!.GetValue<string>());
        Assert.Equal("https://api.oficina/webhooks/mercadopago", corpo["notification_url"]!.GetValue<string>());
        Assert.Equal("maria@example.com", corpo["payer"]!["email"]!.GetValue<string>());
        Assert.True(corpo["expires"]!.GetValue<bool>());
        Assert.Equal("2026-11-18T12:00:00.000+00:00", corpo["expiration_date_to"]!.GetValue<string>());
    }

    [Fact]
    public async Task Sem_sandbox_usa_o_init_point_e_sem_email_nao_manda_payer()
    {
        _handler.Responder(HttpStatusCode.Created, """{"id":"1","init_point":"https://mp/init","sandbox_init_point":"https://mp/sandbox"}""");

        var cobranca = await Gateway(sandbox: false).CriarCobrancaAsync(new NovaCobranca(PagamentoId, "OS-1", 10m, null, DateTime.UtcNow));

        Assert.Equal("https://mp/init", cobranca.LinkPagamento);
        Assert.Null(JsonNode.Parse(_handler.Requisicoes[0].Corpo!)!["payer"]);
    }

    [Theory]
    [InlineData("approved", StatusNoGateway.Aprovado)]
    [InlineData("rejected", StatusNoGateway.Recusado)]
    [InlineData("cancelled", StatusNoGateway.Recusado)]
    [InlineData("refunded", StatusNoGateway.Estornado)]
    [InlineData("charged_back", StatusNoGateway.Estornado)]
    [InlineData("pending", StatusNoGateway.Pendente)]
    [InlineData("in_process", StatusNoGateway.Pendente)]
    [InlineData(null, StatusNoGateway.Pendente)]
    public void Traduz_o_status_do_Mercado_Pago(string? status, StatusNoGateway esperado)
    {
        Assert.Equal(esperado, MercadoPagoGateway.Traduzir(status));
    }

    [Fact]
    public async Task Consulta_o_pagamento_e_le_o_external_reference()
    {
        _handler.Responder(HttpStatusCode.OK, $$"""
            {"id":1234567890,"status":"rejected","status_detail":"cc_rejected_insufficient_amount",
             "external_reference":"{{PagamentoId}}","transaction_amount":329.8}
            """);

        var situacao = await Gateway().ConsultarPagamentoAsync("1234567890");

        Assert.Equal(new SituacaoPagamento("1234567890", PagamentoId, StatusNoGateway.Recusado, 329.8m, "cc_rejected_insufficient_amount"), situacao);
        Assert.Equal("https://mp.teste/v1/payments/1234567890", _handler.Requisicoes[0].Url);
        Assert.Null(_handler.Requisicoes[0].Idempotencia);
    }

    [Fact]
    public async Task Pagamento_inexistente_ou_de_outro_sistema()
    {
        _handler.Responder(HttpStatusCode.NotFound, """{"message":"Payment not found"}""");
        Assert.Null(await Gateway().ConsultarPagamentoAsync("1"));

        _handler.Responder(HttpStatusCode.OK, """{"id":2,"status":"approved","external_reference":"pedido-123","transaction_amount":1}""");
        Assert.Null((await Gateway().ConsultarPagamentoAsync("2"))!.PagamentoId);
    }

    [Fact]
    public async Task Estorna_com_a_chave_de_idempotencia()
    {
        var chave = Guid.NewGuid();
        _handler.Responder(HttpStatusCode.Created, """{"id":987654,"status":"approved"}""");

        var estorno = await Gateway().EstornarAsync("1234567890", chave);

        Assert.Equal(new EstornoRealizado("987654"), estorno);
        var req = _handler.Requisicoes[0];
        Assert.Equal("https://mp.teste/v1/payments/1234567890/refunds", req.Url);
        Assert.Equal(chave.ToString(), req.Idempotencia);
        Assert.Equal("{}", req.Corpo);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Erro_transitorio_vira_gateway_indisponivel(HttpStatusCode status)
    {
        _handler.Responder(status, "{}");

        await Assert.ThrowsAsync<GatewayIndisponivelException>(() => Gateway().EstornarAsync("1", Guid.NewGuid()));
        await Assert.ThrowsAsync<GatewayIndisponivelException>(() => Gateway().ConsultarPagamentoAsync("1"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Erro_4xx_vira_recusa_do_gateway(HttpStatusCode status)
    {
        _handler.Responder(status, """{"message":"invalid"}""");

        await Assert.ThrowsAsync<GatewayRecusouException>(() =>
            Gateway().CriarCobrancaAsync(new NovaCobranca(PagamentoId, "OS-1", 10m, null, DateTime.UtcNow)));
    }

    [Fact]
    public async Task Falha_de_rede_vira_gateway_indisponivel()
    {
        _handler.Falhar = new HttpRequestException("conexão recusada");

        await Assert.ThrowsAsync<GatewayIndisponivelException>(() => Gateway().EstornarAsync("1", Guid.NewGuid()));
        await Assert.ThrowsAsync<GatewayIndisponivelException>(() => Gateway().ConsultarPagamentoAsync("1"));
    }

    [Fact]
    public async Task Resilience_handler_desistindo_vira_gateway_indisponivel()
    {
        // Tempo total esgotado ou circuit breaker aberto: o Polly lança ExecutionRejectedException.
        _handler.Falhar = new Polly.Timeout.TimeoutRejectedException("tempo total esgotado");
        await Assert.ThrowsAsync<GatewayIndisponivelException>(() =>
            Gateway().CriarCobrancaAsync(new NovaCobranca(PagamentoId, "OS-1", 10m, null, DateTime.UtcNow)));

        _handler.Falhar = new Polly.CircuitBreaker.BrokenCircuitException("circuito aberto");
        await Assert.ThrowsAsync<GatewayIndisponivelException>(() => Gateway().ConsultarPagamentoAsync("1"));
    }

    [Fact]
    public async Task Preferencia_sem_link_e_indisponivel()
    {
        _handler.Responder(HttpStatusCode.Created, """{"id":"1"}""");

        await Assert.ThrowsAsync<GatewayIndisponivelException>(() =>
            Gateway().CriarCobrancaAsync(new NovaCobranca(PagamentoId, "OS-1", 10m, null, DateTime.UtcNow)));
    }

    private sealed record Requisicao(HttpMethod Metodo, string Url, string? Authorization, string? Idempotencia, string? Corpo);

    private sealed class HandlerFalso : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _corpo = "{}";

        public List<Requisicao> Requisicoes { get; } = [];

        public Exception? Falhar { get; set; }

        public void Responder(HttpStatusCode status, string corpo)
        {
            _status = status;
            _corpo = corpo;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Falhar is not null)
                throw Falhar;

            Requisicoes.Add(new Requisicao(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Idempotency-Key", out var v) ? v.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(_status) { Content = new StringContent(_corpo, Encoding.UTF8, "application/json") };
        }
    }
}
