using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.UnitTests.UseCases;

public class ComandosSagaUseCasesTests
{
    private readonly Cenario _c = new();

    [Fact]
    public async Task GerarOrcamento_cria_envia_e_responde_com_o_total_calculado()
    {
        var resultado = await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar() with { ValorTotal = 999m });

        Assert.Equal(ResultadoProcessamento.Processada, resultado);
        var orcamento = _c.Orcamento;
        Assert.Equal(StatusOrcamento.Enviado, orcamento.Status);
        Assert.Equal(165.90m, orcamento.ValorTotal);

        var evento = Assert.IsType<OrcamentoGerado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
        Assert.Equal(165.90m, evento.ValorTotal);
        Assert.Equal(Cenario.T0.AddHours(48), evento.ValidoAte);
        Assert.Equal(Cenario.Correlacao, evento.CorrelationId);
        Assert.Equal("OS-0042", evento.NumeroOS);

        var email = Assert.Single(_c.Repositorio.Notificacoes);
        Assert.Contains($"https://oficina/orcamentos/{orcamento.Id}", email.Corpo, StringComparison.Ordinal);
        Assert.Equal(1, _c.Repositorio.Gravacoes);
    }

    [Fact]
    public async Task GerarOrcamento_repetido_nao_gera_outro()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());

        var repetido = await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());

        Assert.Equal(ResultadoProcessamento.Ignorada, repetido);
        Assert.Single(_c.Repositorio.Orcamentos);
        Assert.Single(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Cliente_sem_email_recebe_o_orcamento_sem_notificacao()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar(email: null));

        Assert.Empty(_c.Repositorio.Notificacoes);
        Assert.Equal(StatusOrcamento.Enviado, _c.Orcamento.Status);
    }

    [Fact]
    public async Task GerarOrcamento_sem_itens_e_invalido()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar() with { Itens = [] }));
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Cancelar_cancela_o_orcamento_e_o_pagamento_pendente()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();

        var resultado = await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());

        Assert.Equal(ResultadoProcessamento.Processada, resultado);
        Assert.Equal(StatusOrcamento.Cancelado, _c.Orcamento.Status);
        Assert.Equal(StatusPagamento.Cancelado, pagamento.Status);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Cancelar_depois_do_pagamento_aprovado_estorna_sozinho()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.PagarAsync(pagamento);
        _c.Publicador.Publicadas.Clear();

        await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());

        Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        Assert.NotNull(pagamento.RefundId);
        var evento = Assert.IsType<PagamentoEstornado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal(165.90m, evento.Valor);
    }

    [Fact]
    public async Task Cancelar_de_novo_e_ignorado()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());
        await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());

        Assert.Equal(ResultadoProcessamento.Ignorada, await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar()));
    }

    [Fact]
    public async Task Cancelamento_antes_do_pedido_impede_gerar_cobranca()
    {
        var cancelamento = await _c.Comandos.CancelarOrcamentoAsync(Cenario.Cancelar());
        var pedidoAtrasado = await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());

        Assert.Equal(ResultadoProcessamento.Processada, cancelamento);
        Assert.Equal(ResultadoProcessamento.Ignorada, pedidoAtrasado);
        Assert.Equal(StatusOrcamento.Cancelado, _c.Orcamento.Status);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Estornar_pagamento_aprovado_responde_PagamentoEstornado_e_cancela_o_orcamento()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.PagarAsync(pagamento);
        _c.Publicador.Publicadas.Clear();

        var resultado = await _c.Comandos.EstornarPagamentoAsync(Estornar(pagamento.Id));

        Assert.Equal(ResultadoProcessamento.Processada, resultado);
        Assert.Equal(StatusPagamento.Estornado, pagamento.Status);
        Assert.Equal(StatusOrcamento.Cancelado, _c.Orcamento.Status);
        Assert.Contains("Execução rejeitada", _c.Orcamento.MotivoCancelamento, StringComparison.Ordinal);
        Assert.IsType<PagamentoEstornado>(Assert.Single(_c.Publicador.Publicadas));
    }

    [Fact]
    public async Task Estorno_repetido_nao_estorna_de_novo_mas_responde_de_novo()
    {
        var pagamento = await _c.AprovadoComPagamentoPendenteAsync();
        await _c.PagarAsync(pagamento);
        await _c.Comandos.EstornarPagamentoAsync(Estornar(pagamento.Id));
        var refund = pagamento.RefundId;
        _c.Publicador.Publicadas.Clear();

        var repetido = await _c.Comandos.EstornarPagamentoAsync(Estornar(pagamento.Id));

        Assert.Equal(ResultadoProcessamento.Ignorada, repetido);
        Assert.Equal(refund, pagamento.RefundId);
        Assert.IsType<PagamentoEstornado>(Assert.Single(_c.Publicador.Publicadas));
    }

    [Fact]
    public async Task Estornar_pagamento_que_nao_foi_aprovado_e_impossivel()
    {
        var pendente = await _c.AprovadoComPagamentoPendenteAsync();

        await Assert.ThrowsAsync<EstornoImpossivelException>(() => _c.Comandos.EstornarPagamentoAsync(Estornar(pendente.Id)));
        await Assert.ThrowsAsync<EstornoImpossivelException>(() => _c.Comandos.EstornarPagamentoAsync(Estornar(Guid.NewGuid())));
        Assert.Empty(_c.Publicador.Publicadas);
    }

    private static EstornarPagamento Estornar(Guid pagamentoId) => new()
    {
        CorrelationId = Cenario.Correlacao,
        OsId = 42,
        NumeroOS = "OS-0042",
        PagamentoId = pagamentoId,
        Motivo = "Execução rejeitada"
    };
}
