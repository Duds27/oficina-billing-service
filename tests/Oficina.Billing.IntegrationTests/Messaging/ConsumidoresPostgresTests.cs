using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Domain;
using Oficina.Billing.Infrastructure.Pagamentos;
using Oficina.Billing.Infrastructure.Persistence;
using Oficina.Billing.IntegrationTests.Infraestrutura;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Messaging;

/// <summary>
/// Consumidores com o PostgreSQL de verdade, o outbox/inbox do MassTransit no banco e o
/// transporte em memória do test harness. Gateway Fake.
/// </summary>
[Collection(ColecaoPostgres.Nome)]
public sealed class ConsumidoresPostgresTests : IAsyncLifetime
{
    private static readonly Guid Correlacao = Guid.Parse("3f2c9a4e-8d1b-4c55-9a7e-2b6f0d9e1a11");

    private readonly PostgresFixture _postgres;
    private ApiFactory _factory = null!;
    private ITestHarness _harness = null!;

    public ConsumidoresPostgresTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_postgres.NovoBanco());
        _factory.CreateClient().Dispose(); // sobe o host (migrations + bus)
        _harness = _factory.Services.GetTestHarness();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task EnviarAsync<T>(string fila, T mensagem, Guid? messageId = null) where T : class
    {
        var endpoint = await _harness.Bus.GetSendEndpoint(new Uri($"queue:{fila}"));
        await endpoint.Send(mensagem, c => c.MessageId = messageId ?? NewId.NextGuid());
    }

    /// <summary>O evento chegou à captura (o "os-service" do teste).</summary>
    private Task<T> PublicadoAsync<T>(Func<T, bool> filtro) where T : class =>
        Esperar.AteAsync(
            () => Task.FromResult(_harness.Consumed.Select<T>(m => filtro(m.Context.Message)).FirstOrDefault()?.Context.Message),
            _ => true, $"{typeof(T).Name} não foi publicado.");

    private async Task<T?> NoBancoAsync<T>(Func<BillingDbContext, Task<T?>> consulta) where T : class
    {
        await using var escopo = _factory.Services.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<BillingDbContext>());
    }

    private static GerarOrcamento Gerar(Guid? correlacao = null, int osId = 42) => new()
    {
        CorrelationId = correlacao ?? Correlacao,
        OsId = osId,
        NumeroOS = $"OS-{osId:D4}",
        OcorridoEm = DateTime.UtcNow,
        Cliente = new ClienteInfo { Id = 7, Nome = "Maria", Email = "maria@example.com" },
        Itens = [new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de óleo", Quantidade = 1, PrecoUnitario = 120m }],
        ValorTotal = 120m
    };

    /// <summary>Orçamento aprovado com o pagamento pendente, gravado direto no banco.</summary>
    private async Task<(Orcamento Orcamento, Pagamento Pagamento)> AprovadoAsync(Guid correlacao, int osId)
    {
        var orcamento = Orcamento.Gerar(correlacao, osId, $"OS-{osId:D4}", new ClienteOrcamento(7, "Maria", null),
            [new ItemOrcamento("Servico", 3, "Troca de óleo", 1, 120m)], TimeSpan.FromHours(1), DateTime.UtcNow);
        orcamento.MarcarEnviado(DateTime.UtcNow);
        orcamento.Aprovar(DateTime.UtcNow);
        var pagamento = Pagamento.Criar(orcamento, DateTime.UtcNow);
        pagamento.RegistrarCobranca("pref", "https://checkout/pref");

        await using var escopo = _factory.Services.CreateAsyncScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IBillingRepositorio>();
        repositorio.Adicionar(orcamento);
        repositorio.Adicionar(pagamento);
        await repositorio.SalvarAsync();
        return (orcamento, pagamento);
    }

    private async Task<string> PagarAsync(Pagamento pagamento)
    {
        var mpPaymentId = _factory.Services.GetRequiredService<ISimuladorPagamentos>().Simular(pagamento.Id, pagamento.Valor, aprovado: true);
        await _harness.Bus.Publish(new ProcessarNotificacaoPagamento { MpPaymentId = mpPaymentId, RecebidaEm = DateTime.UtcNow });
        return mpPaymentId;
    }

    [Fact]
    public async Task GerarOrcamento_grava_envia_o_email_e_responde_OrcamentoGerado()
    {
        await EnviarAsync(FilasBilling.GerarOrcamento, Gerar());

        var evento = await PublicadoAsync<OrcamentoGerado>(m => m.CorrelationId == Correlacao);
        var orcamento = (await NoBancoAsync(db => db.Orcamentos.FirstOrDefaultAsync(o => o.CorrelationId == Correlacao)))!;
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        Assert.Equal(StatusOrcamento.Enviado, orcamento.Status);
        Assert.Equal(120m, evento.ValorTotal);

        // O envio simulado marca o e-mail como enviado.
        await Esperar.AteAsync(
            () => NoBancoAsync(db => db.Notificacoes.FirstOrDefaultAsync(n => n.OrcamentoId == orcamento.Id && n.EnviadaEm != null)),
            _ => true, "O e-mail do orçamento não foi enviado.");
    }

    [Fact]
    public async Task Pagamento_aprovado_no_gateway_vira_PagamentoConfirmado()
    {
        var correlacao = Guid.NewGuid();
        var (_, pagamento) = await AprovadoAsync(correlacao, 50);

        var mpPaymentId = await PagarAsync(pagamento);

        var confirmado = await PublicadoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);
        Assert.Equal(mpPaymentId, confirmado.MpPaymentId);
        Assert.Equal(correlacao, confirmado.CorrelationId);
        var gravado = (await NoBancoAsync(db => db.Pagamentos.FirstOrDefaultAsync(p => p.Id == pagamento.Id)))!;
        Assert.Equal(StatusPagamento.Aprovado, gravado.Status);
    }

    [Fact]
    public async Task Inbox_descarta_o_estorno_repetido_com_o_mesmo_MessageId()
    {
        var correlacao = Guid.NewGuid();
        var (_, pagamento) = await AprovadoAsync(correlacao, 51);
        await PagarAsync(pagamento);
        await PublicadoAsync<PagamentoConfirmado>(m => m.PagamentoId == pagamento.Id);

        var messageId = NewId.NextGuid();
        var estorno = new EstornarPagamento { CorrelationId = correlacao, OsId = 51, NumeroOS = "OS-0051", PagamentoId = pagamento.Id, Motivo = "Execução rejeitada" };
        await EnviarAsync(FilasBilling.EstornarPagamento, estorno, messageId);
        await PublicadoAsync<PagamentoEstornado>(m => m.PagamentoId == pagamento.Id);
        await EnviarAsync(FilasBilling.EstornarPagamento, estorno, messageId);

        // Sem o inbox, a cópia seria processada e responderia PagamentoEstornado de novo.
        await Esperar.AteAsync(
            () => _harness.Consumed.Select<EstornarPagamento>(m => m.Context.MessageId == messageId).Any(),
            "O estorno não foi consumido.");
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Single(_harness.Consumed.Select<PagamentoEstornado>(m => m.Context.Message.PagamentoId == pagamento.Id));
        var gravado = (await NoBancoAsync(db => db.Pagamentos.FirstOrDefaultAsync(p => p.Id == pagamento.Id)))!;
        Assert.Equal(StatusPagamento.Estornado, gravado.Status);
    }

    [Fact]
    public async Task Estorno_impossivel_vai_para_o_erro_e_dispara_o_alerta_critico()
    {
        var correlacao = Guid.NewGuid();
        var (_, pendente) = await AprovadoAsync(correlacao, 52);

        await EnviarAsync(FilasBilling.EstornarPagamento,
            new EstornarPagamento { CorrelationId = correlacao, OsId = 52, NumeroOS = "OS-0052", PagamentoId = pendente.Id, Motivo = "teste" });

        await Esperar.AteAsync(
            () => _harness.Consumed.Select<Fault<EstornarPagamento>>(f => f.Context.Message.Message.PagamentoId == pendente.Id).Any(),
            "A falha do estorno não chegou ao consumidor do alerta.");
        Assert.Empty(_harness.Consumed.Select<PagamentoEstornado>(m => m.Context.Message.PagamentoId == pendente.Id));
    }

    [Fact]
    public async Task CancelarOrcamento_cancela_no_banco()
    {
        var correlacao = Guid.NewGuid();
        await EnviarAsync(FilasBilling.GerarOrcamento, Gerar(correlacao, 53));
        await PublicadoAsync<OrcamentoGerado>(m => m.CorrelationId == correlacao);

        await EnviarAsync(FilasBilling.CancelarOrcamento, new CancelarOrcamento { CorrelationId = correlacao, OsId = 53, NumeroOS = "OS-0053", Motivo = "Prazo expirado" });

        var cancelado = await Esperar.AteAsync(
            () => NoBancoAsync(db => db.Orcamentos.FirstOrDefaultAsync(o => o.CorrelationId == correlacao)),
            o => o.Status == StatusOrcamento.Cancelado, "O orçamento não foi cancelado.");
        Assert.Equal("Prazo expirado", cancelado.MotivoCancelamento);
    }
}
