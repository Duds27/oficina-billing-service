using System.Net;
using System.Net.Http.Json;
using MassTransit;
using MassTransit.Testing;
using Oficina.Billing.Application.DTOs;
using Oficina.Billing.IntegrationTests.Infraestrutura;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Api;

/// <summary>API com PostgreSQL, outbox no banco, transporte em memória e gateway Fake.</summary>
[Collection(ColecaoPostgres.Nome)]
public sealed class OrcamentosApiTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private ITestHarness _harness = null!;

    public OrcamentosApiTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.NovoBanco());
        _factory.CreateClient().Dispose();
        _harness = _factory.Services.GetTestHarness();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private HttpClient Cliente(int clienteId = 7) => _factory.CreateClient().Autenticado(Tokens.DeCliente(clienteId));

    private HttpClient Oficina(string papel = "Admin") => _factory.CreateClient().Autenticado(Tokens.DaOficina(papel));

    private Task<T> RecebidoAsync<T>(Func<T, bool> filtro) where T : class =>
        Esperar.AteAsync(
            () => Task.FromResult(_harness.Consumed.Select<T>(m => filtro(m.Context.Message)).FirstOrDefault()?.Context.Message),
            _ => true, $"{typeof(T).Name} não chegou.");

    /// <summary>A saga pede o orçamento; devolve o id quando ele estiver enviado ao cliente.</summary>
    private async Task<Guid> OrcamentoEnviadoAsync(int osId, int clienteId = 7)
    {
        var correlacao = Guid.NewGuid();
        var endpoint = await _harness.Bus.GetSendEndpoint(new Uri($"queue:{FilasBilling.GerarOrcamento}"));
        await endpoint.Send(new GerarOrcamento
        {
            CorrelationId = correlacao,
            OsId = osId,
            NumeroOS = $"OS-{osId:D4}",
            OcorridoEm = DateTime.UtcNow,
            Cliente = new ClienteInfo { Id = clienteId, Nome = "Maria", Email = "maria@example.com" },
            Itens =
            [
                new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de pastilhas", Quantidade = 1, PrecoUnitario = 150m },
                new ItemOrcamentoInfo { Tipo = "Peca", ReferenciaId = 9, Descricao = "Pastilha", Quantidade = 2, PrecoUnitario = 89.90m }
            ],
            ValorTotal = 329.80m
        });
        return (await RecebidoAsync<OrcamentoGerado>(m => m.CorrelationId == correlacao)).OrcamentoId;
    }

    [Fact]
    public async Task Fluxo_com_o_gateway_Fake_da_aprovacao_ao_PagamentoConfirmado()
    {
        var id = await OrcamentoEnviadoAsync(42);
        using var maria = Cliente();

        var daOs = await maria.GetFromJsonAsync<List<OrcamentoDto>>("/api/orcamentos?osId=42");
        var orcamento = Assert.Single(daOs!);
        Assert.Equal("Enviado", orcamento.Status);
        Assert.Equal(329.80m, orcamento.ValorTotal);
        Assert.Equal(2, orcamento.Itens.Count);

        var aprovacao = await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true });
        Assert.Equal(HttpStatusCode.OK, aprovacao.StatusCode);
        var aprovado = (await aprovacao.Content.ReadFromJsonAsync<OrcamentoDto>())!;
        Assert.Equal("Aprovado", aprovado.Status);
        var pagamentoId = aprovado.Pagamento!.Id;
        Assert.StartsWith("https://pagamento.fake.local/checkout/", aprovado.Pagamento.LinkPagamento, StringComparison.Ordinal);

        var evento = await RecebidoAsync<OrcamentoAprovado>(m => m.OrcamentoId == id);
        Assert.Equal(pagamentoId, evento.PagamentoId);
        Assert.Equal(aprovado.Pagamento.LinkPagamento, evento.LinkPagamento);

        // O cliente "paga" (gateway Fake): o webhook simulado vira PagamentoConfirmado.
        using var anonimo = _factory.CreateClient();
        var simulacao = await anonimo.PostAsync($"/dev/pagamentos/{pagamentoId}/simular?resultado=aprovado", null);
        Assert.Equal(HttpStatusCode.Accepted, simulacao.StatusCode);

        var confirmado = await RecebidoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamentoId);
        Assert.Equal(329.80m, confirmado.Valor);
        Assert.StartsWith("fake-", confirmado.MpPaymentId, StringComparison.Ordinal);
        var pagamento = await Esperar.AteAsync(
            () => maria.GetFromJsonAsync<PagamentoDto>($"/api/pagamentos/{pagamentoId}"),
            p => p.Status == "Aprovado", "O pagamento não ficou aprovado.");
        Assert.Equal(confirmado.MpPaymentId, pagamento.MpPaymentId);
    }

    [Fact]
    public async Task Recusa_do_cliente_publica_OrcamentoRecusado()
    {
        var id = await OrcamentoEnviadoAsync(43);
        using var maria = Cliente();

        var resposta = await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = false, motivo = "Vou pensar" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Vou pensar", (await RecebidoAsync<OrcamentoRecusado>(m => m.OrcamentoId == id)).Motivo);
    }

    [Fact]
    public async Task Pagamento_recusado_no_gateway_publica_PagamentoRecusado()
    {
        var id = await OrcamentoEnviadoAsync(44);
        using var maria = Cliente();
        var aprovado = (await (await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true })).Content.ReadFromJsonAsync<OrcamentoDto>())!;

        using var anonimo = _factory.CreateClient();
        await anonimo.PostAsync($"/dev/pagamentos/{aprovado.Pagamento!.Id}/simular?resultado=recusado", null);

        var recusado = await RecebidoAsync<PagamentoRecusado>(m => m.PagamentoId == aprovado.Pagamento.Id);
        Assert.Contains("simulado", recusado.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Um_cliente_nao_aprova_orcamento_de_outro()
    {
        var id = await OrcamentoEnviadoAsync(45, clienteId: 7);
        using var joao = Cliente(clienteId: 8);

        Assert.Equal(HttpStatusCode.Forbidden, (await joao.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await joao.GetAsync($"/api/orcamentos/{id}")).StatusCode);
        Assert.Empty((await joao.GetFromJsonAsync<List<OrcamentoDto>>("/api/orcamentos?osId=45"))!);

        using var maria = Cliente(7);
        Assert.Equal("Enviado", (await maria.GetFromJsonAsync<OrcamentoDto>($"/api/orcamentos/{id}"))!.Status);
    }

    [Fact]
    public async Task Atendente_registra_a_decisao_e_mecanico_nao()
    {
        var id = await OrcamentoEnviadoAsync(46);
        using var mecanico = Oficina("Mecanico");
        using var atendente = Oficina("Admin");

        Assert.Equal(HttpStatusCode.Forbidden, (await mecanico.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mecanico.GetAsync($"/api/orcamentos/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await atendente.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true })).StatusCode);
    }

    [Fact]
    public async Task Decidir_duas_vezes_responde_409()
    {
        var id = await OrcamentoEnviadoAsync(47);
        using var maria = Cliente();
        await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true });

        var denovo = await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = false });

        Assert.Equal(HttpStatusCode.Conflict, denovo.StatusCode);
        Assert.Contains("Aprovado", await denovo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Erros_de_entrada_e_de_autenticacao()
    {
        using var maria = Cliente();
        using var anonimo = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/orcamentos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/orcamentos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maria.GetAsync($"/api/pagamentos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await maria.PostAsJsonAsync($"/api/orcamentos/{Guid.NewGuid()}/aprovacao", new { motivo = "sem decisão" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonimo.PostAsync($"/dev/pagamentos/{Guid.NewGuid()}/simular?resultado=talvez", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.PostAsync($"/dev/pagamentos/{Guid.NewGuid()}/simular?resultado=aprovado", null)).StatusCode);
    }

    [Fact]
    public async Task Orcamento_vencido_nao_e_aprovado()
    {
        await using var curto = new ApiFactory(_postgres.NovoBanco()) { Configuracoes = { ["Orcamento:Validade"] = "00:00:01" } };
        curto.CreateClient().Dispose();
        var harness = curto.Services.GetTestHarness();
        var correlacao = Guid.NewGuid();
        await (await harness.Bus.GetSendEndpoint(new Uri($"queue:{FilasBilling.GerarOrcamento}"))).Send(new GerarOrcamento
        {
            CorrelationId = correlacao,
            OsId = 48,
            NumeroOS = "OS-0048",
            Cliente = new ClienteInfo { Id = 7, Nome = "Maria" },
            Itens = [new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 1, Descricao = "x", Quantidade = 1, PrecoUnitario = 10m }]
        });
        await Esperar.AteAsync(() => harness.Consumed.Select<OrcamentoGerado>(m => m.Context.Message.CorrelationId == correlacao).Any(), "Orçamento não gerado.");
        var id = harness.Consumed.Select<OrcamentoGerado>(m => m.Context.Message.CorrelationId == correlacao).First().Context.Message.OrcamentoId;
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        using var maria = curto.CreateClient().Autenticado(Tokens.DeCliente(7));

        var resposta = await maria.PostAsJsonAsync($"/api/orcamentos/{id}/aprovacao", new { aprovado = true });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("Expirado", (await maria.GetFromJsonAsync<OrcamentoDto>($"/api/orcamentos/{id}"))!.Status);
    }
}
