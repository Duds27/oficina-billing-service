using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Domain;
using Oficina.Billing.Infrastructure.Pagamentos;
using Oficina.Contracts;

namespace Oficina.Billing.UnitTests.UseCases;

public class PagamentosUseCasesTests
{
    private readonly Cenario _c = new();

    [Fact]
    public async Task Pagamento_aprovado_confirma_e_publica_PagamentoConfirmado()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();

        var mpPaymentId = await _c.PagarAsync(pagamento);

        Assert.Equal(StatusPagamento.Aprovado, pagamento.Status);
        Assert.Equal(mpPaymentId, pagamento.MpPaymentId);
        var evento = Assert.IsType<PagamentoConfirmado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal(mpPaymentId, evento.MpPaymentId);
        Assert.Equal(165.90m, evento.Valor);
        Assert.Equal(Cenario.Correlacao, evento.CorrelationId);
    }

    [Fact]
    public async Task Notificacao_repetida_nao_publica_de_novo()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        var mpPaymentId = await _c.PagarAsync(pagamento);

        var repetida = await _c.Pagamentos.ProcessarNotificacaoAsync(new ProcessarNotificacaoPagamento { MpPaymentId = mpPaymentId });

        Assert.Equal(ResultadoProcessamento.Ignorada, repetida);
        Assert.Single(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Pagamento_recusado_publica_PagamentoRecusado_com_o_motivo()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();

        await _c.PagarAsync(pagamento, aprovado: false);

        Assert.Equal(StatusPagamento.Recusado, pagamento.Status);
        var evento = Assert.IsType<PagamentoRecusado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal(FakeGatewayPagamento.MotivoRecusaSimulada, evento.Motivo);
    }

    [Fact]
    public async Task Aprovado_depois_do_cancelamento_e_estornado_sozinho()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());

        await _c.PagarAsync(pagamento);

        Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        Assert.IsType<PagamentoEstornado>(Assert.Single(_c.Publicador.Publicadas));
    }

    [Fact]
    public async Task Aprovado_depois_de_recusado_e_estornado_sozinho()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.PagarAsync(pagamento, aprovado: false);
        _c.Publicador.Publicadas.Clear();

        // O comprador tentou outro cartão na mesma preferência.
        await _c.PagarAsync(pagamento);

        Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        Assert.IsType<PagamentoEstornado>(Assert.Single(_c.Publicador.Publicadas));
    }

    [Fact]
    public async Task Notificacao_de_pagamento_desconhecido_e_ignorada()
    {
        var desconhecido = await _c.Pagamentos.ProcessarNotificacaoAsync(new ProcessarNotificacaoPagamento { MpPaymentId = "nao-existe" });
        var deOutroSistema = _c.Gateway.Simular(Guid.NewGuid(), 10m, aprovado: true);
        var semPagamento = await _c.Pagamentos.ProcessarNotificacaoAsync(new ProcessarNotificacaoPagamento { MpPaymentId = deOutroSistema });

        Assert.Equal(ResultadoProcessamento.Ignorada, desconhecido);
        Assert.Equal(ResultadoProcessamento.Ignorada, semPagamento);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Pagamento_ja_estornado_no_gateway_nao_muda_nada()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        var mpPaymentId = _c.Gateway.Simular(pagamento.Id, pagamento.Valor, aprovado: true);
        await _c.Gateway.EstornarAsync(mpPaymentId, Guid.NewGuid());

        var resultado = await _c.Pagamentos.ProcessarNotificacaoAsync(new ProcessarNotificacaoPagamento { MpPaymentId = mpPaymentId });

        Assert.Equal(ResultadoProcessamento.Ignorada, resultado);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
    }
}
