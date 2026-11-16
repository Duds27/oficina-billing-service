using System.Net;
using System.Net.Http.Json;
using Oficina.Billing.Application.DTOs;
using Oficina.Billing.IntegrationTests.Api;
using Oficina.Billing.IntegrationTests.Infraestrutura;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Pagamentos;

/// <summary>
/// Ponta a ponta: RabbitMQ de verdade, PostgreSQL, o Mercado Pago no WireMock e um bus no papel
/// do os-service, do pedido de orçamento ao estorno.
/// </summary>
[Collection(ColecaoMensageria.Nome)]
public sealed class FluxoRabbitMqTests : IAsyncLifetime
{
    private readonly MensageriaFixture _fixture;
    private readonly MercadoPagoSimulado _mp = new();
    private RabbitMqApiFactory _factory = null!;
    private OsServiceSimulado _os = null!;

    public FluxoRabbitMqTests(MensageriaFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _factory = new RabbitMqApiFactory(_fixture);
        _mp.Configurar(_factory);
        _os = await OsServiceSimulado.IniciarAsync(_fixture);
    }

    public async Task DisposeAsync()
    {
        await _os.DisposeAsync();
        await _factory.DisposeAsync();
        _mp.Dispose();
    }

    [Fact]
    public async Task Do_pedido_de_orcamento_ao_estorno_pelo_RabbitMQ()
    {
        using var maria = _factory.CreateClient().Autenticado(Tokens.DeCliente(7));
        await Esperar.AteAsync(
            async () => (await maria.GetAsync("/health/ready")).StatusCode == HttpStatusCode.OK ? "pronta" : null,
            _ => true, "A API não ficou pronta.", segundos: 60);
        var correlacao = Guid.NewGuid();

        // 1. A saga pede o orçamento.
        await _os.EnviarAsync(FilasBilling.GerarOrcamento, new GerarOrcamento
        {
            CorrelationId = correlacao,
            OsId = 70,
            NumeroOS = "OS-0070",
            OcorridoEm = DateTime.UtcNow,
            Cliente = new ClienteInfo { Id = 7, Nome = "Maria", Email = "maria@example.com" },
            Itens = [new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de pastilhas", Quantidade = 1, PrecoUnitario = 250m }],
            ValorTotal = 250m
        });
        var gerado = await _os.EsperarAsync<OrcamentoGerado>(m => m.CorrelationId == correlacao);
        Assert.Equal(250m, gerado.ValorTotal);
        Assert.Contains("urn:message:oficina:orcamento-gerado:v1", _os.Recebidos.First(r => r.Mensagem is OrcamentoGerado).Tipos);

        // 2. A cliente aprova: preferência no Mercado Pago e o link no OrcamentoAprovado.
        var aprovacao = await maria.PostAsJsonAsync($"/api/orcamentos/{gerado.OrcamentoId}/aprovacao", new { aprovado = true });
        Assert.Equal(HttpStatusCode.OK, aprovacao.StatusCode);
        var pagamento = (await aprovacao.Content.ReadFromJsonAsync<OrcamentoDto>())!.Pagamento!;
        var aprovado = await _os.EsperarAsync<OrcamentoAprovado>(m => m.CorrelationId == correlacao);
        Assert.Equal(pagamento.Id, aprovado.PagamentoId);
        Assert.Contains("sandbox.mercadopago.com.br", aprovado.LinkPagamento, StringComparison.Ordinal);

        // 3. Ela paga; o webhook assinado chega e o billing consulta o Mercado Pago.
        _mp.Pagamento("7777777777", pagamento.Id, "approved", 250m);
        _mp.Estorno("7777777777", 1234);
        using (var anonimo = _factory.CreateClient())
        using (var webhook = WebhookApiTests.Notificacao("7777777777", segredo: MercadoPagoSimulado.SegredoWebhook))
            Assert.Equal(HttpStatusCode.OK, (await anonimo.SendAsync(webhook)).StatusCode);
        var confirmado = await _os.EsperarAsync<PagamentoConfirmado>(m => m.CorrelationId == correlacao);
        Assert.Equal("7777777777", confirmado.MpPaymentId);

        // 4. A execução foi rejeitada: a saga manda estornar (C3).
        await _os.EnviarAsync(FilasBilling.EstornarPagamento, new EstornarPagamento
        {
            CorrelationId = correlacao,
            OsId = 70,
            NumeroOS = "OS-0070",
            OcorridoEm = DateTime.UtcNow,
            PagamentoId = pagamento.Id,
            Motivo = "Execução rejeitada: falha simulada"
        });
        var estornado = await _os.EsperarAsync<PagamentoEstornado>(m => m.CorrelationId == correlacao);
        Assert.Equal(250m, estornado.Valor);
        Assert.Equal(pagamento.Id.ToString(), Assert.Single(_mp.Chamadas("/v1/payments/7777777777/refunds")).Idempotencia);

        var orcamento = await maria.GetFromJsonAsync<OrcamentoDto>($"/api/orcamentos/{gerado.OrcamentoId}");
        Assert.Equal("Cancelado", orcamento!.Status);
        Assert.Equal("Estornado", orcamento.Pagamento!.Status);
    }
}
