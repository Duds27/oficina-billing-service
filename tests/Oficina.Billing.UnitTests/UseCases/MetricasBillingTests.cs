using Oficina.Billing.Application;
using Oficina.Billing.Application.UseCases;
using Oficina.Billing.UnitTests.TestSupport;

namespace Oficina.Billing.UnitTests.UseCases;

/// <summary>O Meter é estático: esta coleção roda sozinha, para outra classe não somar nas contagens.</summary>
[CollectionDefinition(nameof(MetricasBillingColecao), DisableParallelization = true)]
public class MetricasBillingColecao;

/// <summary>Métricas de negócio do billing (Meter Oficina.Billing, Etapa 5).</summary>
[Collection(nameof(MetricasBillingColecao))]
public sealed class MetricasBillingTests : IDisposable
{
    private static readonly Solicitante Maria = new(DaOficina: false, Atendente: false, ClienteId: 7);

    private readonly ColetorDeMetricas _metricas = new(MetricasBilling.NomeMeter);
    private readonly Cenario _c = new();

    public void Dispose() => _metricas.Dispose();

    private string Tag(string instrumento, string tag) => Assert.Single(_metricas.Medicoes(instrumento)).Tags[tag]!;

    [Fact]
    public async Task Aprovacao_e_pagamento_aprovado()
    {
        // Pela decisão do cliente, como a API faz (o AprovadoComPagamentoPendenteAsync aprova direto no domínio).
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());
        await _c.Orcamentos.DecidirAsync(_c.Orcamento.Id, aprovado: true, null, Maria);
        await _c.PagarAsync(_c.Repositorio.Pagamentos.Single());

        Assert.Equal("aprovado", Tag("orcamento.decisao", "resultado"));
        Assert.Equal("aprovado", Tag("pagamento.resultado", "status"));
        Assert.Empty(_metricas.Medicoes("pagamento.estorno"));
    }

    [Fact]
    public async Task Recusa_do_orcamento()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());

        await _c.Orcamentos.DecidirAsync(_c.Orcamento.Id, aprovado: false, "caro", Maria);

        Assert.Equal("recusado", Tag("orcamento.decisao", "resultado"));
    }

    [Fact]
    public async Task Orcamento_vencido_conta_como_expirado()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());
        _c.Relogio.Avancar(TimeSpan.FromHours(49));

        await _c.Orcamentos.DecidirAsync(_c.Orcamento.Id, aprovado: true, null, Maria);

        Assert.Equal("expirado", Tag("orcamento.decisao", "resultado"));
    }

    [Fact]
    public async Task Pagamento_recusado()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();

        await _c.PagarAsync(pagamento, aprovado: false);

        Assert.Equal("recusado", Tag("pagamento.resultado", "status"));
    }

    [Fact]
    public async Task Pagamento_depois_do_cancelamento_e_tardio_e_estornado()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());

        await _c.PagarAsync(pagamento);

        Assert.Equal("tardio", Tag("pagamento.resultado", "status"));
        Assert.Equal(1, _metricas.Soma("pagamento.estorno"));
    }
}
