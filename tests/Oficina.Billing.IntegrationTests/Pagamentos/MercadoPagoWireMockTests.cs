using System.Net;
using System.Net.Http.Json;
using MassTransit;
using MassTransit.Testing;
using Oficina.Billing.Application.DTOs;
using Oficina.Billing.IntegrationTests.Api;
using Oficina.Billing.IntegrationTests.Infraestrutura;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Pagamentos;

/// <summary>
/// O gateway real (<c>MercadoPagoGateway</c>, com o resilience handler) contra o Mercado Pago no
/// WireMock, com PostgreSQL de verdade e o transporte em memória.
/// </summary>
[Collection(ColecaoPostgres.Nome)]
public sealed class MercadoPagoWireMockTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly MercadoPagoSimulado _mp = new();
    private ApiFactory _factory = null!;
    private ITestHarness _harness = null!;
    private HttpClient _maria = null!;

    public MercadoPagoWireMockTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.NovoBanco());
        _mp.Configurar(_factory);
        _maria = _factory.CreateClient().Autenticado(Tokens.DeCliente(7));
        _harness = _factory.Services.GetTestHarness();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _maria.Dispose();
        await _factory.DisposeAsync();
        _mp.Dispose();
    }

    private Task<T> RecebidoAsync<T>(Func<T, bool> filtro) where T : class =>
        Esperar.AteAsync(
            () => Task.FromResult(_harness.Consumed.Select<T>(m => filtro(m.Context.Message)).FirstOrDefault()?.Context.Message),
            _ => true, $"{typeof(T).Name} não chegou.");

    /// <summary>Saga pede o orçamento, a cliente aprova: devolve o pagamento criado.</summary>
    private async Task<(Guid CorrelationId, PagamentoResumoDto Pagamento)> AprovadoAsync(int osId)
    {
        var correlacao = Guid.NewGuid();
        await (await _harness.Bus.GetSendEndpoint(new Uri($"queue:{FilasBilling.GerarOrcamento}"))).Send(new GerarOrcamento
        {
            CorrelationId = correlacao,
            OsId = osId,
            NumeroOS = $"OS-{osId:D4}",
            OcorridoEm = DateTime.UtcNow,
            Cliente = new ClienteInfo { Id = 7, Nome = "Maria", Email = "maria@example.com" },
            Itens = [new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de pastilhas", Quantidade = 1.5m, PrecoUnitario = 100m }]
        });
        var gerado = await RecebidoAsync<OrcamentoGerado>(m => m.CorrelationId == correlacao);

        var resposta = await _maria.PostAsJsonAsync($"/api/orcamentos/{gerado.OrcamentoId}/aprovacao", new { aprovado = true });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (correlacao, (await resposta.Content.ReadFromJsonAsync<OrcamentoDto>())!.Pagamento!);
    }

    private async Task WebhookAsync(string mpPaymentId)
    {
        using var client = _factory.CreateClient();
        using var notificacao = WebhookApiTests.Notificacao(mpPaymentId, segredo: MercadoPagoSimulado.SegredoWebhook);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(notificacao)).StatusCode);
    }

    [Fact]
    public async Task Aprovacao_cria_a_preferencia_e_o_link_vai_no_OrcamentoAprovado()
    {
        var (_, pagamento) = await AprovadoAsync(60);

        Assert.Equal("https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id=123456789-pref", pagamento.LinkPagamento);
        var chamada = Assert.Single(_mp.Chamadas("/checkout/preferences"));
        Assert.Equal(pagamento.Id.ToString(), chamada.Idempotencia);
        Assert.Contains($"\"external_reference\":\"{pagamento.Id}\"", chamada.Corpo, StringComparison.Ordinal);
        Assert.Contains("\"unit_price\":150.0", chamada.Corpo, StringComparison.Ordinal);

        var aprovado = await RecebidoAsync<OrcamentoAprovado>(m => m.PagamentoId == pagamento.Id);
        Assert.Equal(pagamento.LinkPagamento, aprovado.LinkPagamento);
    }

    [Fact]
    public async Task Pagamento_aprovado_no_Mercado_Pago_vira_PagamentoConfirmado()
    {
        var (correlacao, pagamento) = await AprovadoAsync(61);
        _mp.Pagamento("1111111111", pagamento.Id, "approved", 150m);

        await WebhookAsync("1111111111");

        var confirmado = await RecebidoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);
        Assert.Equal("1111111111", confirmado.MpPaymentId);
        Assert.Equal(150m, confirmado.Valor);
        Assert.Equal(correlacao, confirmado.CorrelationId);
    }

    [Fact]
    public async Task Pagamento_recusado_no_Mercado_Pago_vira_PagamentoRecusado()
    {
        var (_, pagamento) = await AprovadoAsync(62);
        _mp.Pagamento("2222222222", pagamento.Id, "rejected", 150m, "cc_rejected_insufficient_amount");

        await WebhookAsync("2222222222");

        var recusado = await RecebidoAsync<PagamentoRecusado>(m => m.PagamentoId == pagamento.Id);
        Assert.Equal("cc_rejected_insufficient_amount", recusado.Motivo);
    }

    [Fact]
    public async Task Webhook_duplicado_confirma_uma_vez_so()
    {
        var (_, pagamento) = await AprovadoAsync(63);
        _mp.Pagamento("3333333333", pagamento.Id, "approved", 150m);

        await WebhookAsync("3333333333");
        await RecebidoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);
        await WebhookAsync("3333333333");

        // A segunda notificação também consulta o Mercado Pago, mas o pagamento já está aprovado.
        await Esperar.AteAsync(() => _mp.Chamadas("/v1/payments/3333333333").Count >= 2, "A segunda notificação não foi processada.");
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Single(_harness.Consumed.Select<PagamentoConfirmado>(m => m.Context.Message.PagamentoId == pagamento.Id));
    }

    [Fact]
    public async Task Falha_temporaria_da_API_do_Mercado_Pago_e_superada_pelo_retry()
    {
        var (_, pagamento) = await AprovadoAsync(64);
        _mp.PagamentoComFalhaTemporaria("4444444444", pagamento.Id, 150m);

        await WebhookAsync("4444444444");

        await RecebidoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);
        Assert.True(_mp.Chamadas("/v1/payments/4444444444").Count >= 2, "Deveria ter tentado de novo depois do 503.");
    }

    [Fact]
    public async Task Estorno_chama_o_Mercado_Pago_com_idempotencia_e_responde_PagamentoEstornado()
    {
        var (correlacao, pagamento) = await AprovadoAsync(65);
        _mp.Pagamento("5555555555", pagamento.Id, "approved", 150m);
        _mp.Estorno("5555555555", 987654321);
        await WebhookAsync("5555555555");
        await RecebidoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);

        await (await _harness.Bus.GetSendEndpoint(new Uri($"queue:{FilasBilling.EstornarPagamento}"))).Send(new EstornarPagamento
        {
            CorrelationId = correlacao,
            OsId = 65,
            NumeroOS = "OS-0065",
            PagamentoId = pagamento.Id,
            Motivo = "Execução rejeitada: falha simulada"
        });

        var estornado = await RecebidoAsync<PagamentoEstornado>(m => m.PagamentoId == pagamento.Id);
        Assert.Equal(150m, estornado.Valor);
        var chamada = Assert.Single(_mp.Chamadas("/v1/payments/5555555555/refunds"));
        Assert.Equal(pagamento.Id.ToString(), chamada.Idempotencia);
        var gravado = await _maria.GetFromJsonAsync<PagamentoDto>($"/api/pagamentos/{pagamento.Id}");
        Assert.Equal("Estornado", gravado!.Status);
    }
}
