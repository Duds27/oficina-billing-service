using Oficina.Billing.Domain;

namespace Oficina.Billing.UnitTests.Domain;

public class PagamentoTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static Pagamento Pendente()
    {
        var orcamento = OrcamentoTests.Enviado(T0);
        orcamento.Aprovar(T0);
        return Pagamento.Criar(orcamento, T0);
    }

    [Fact]
    public void Criar_cobra_o_total_do_orcamento_aprovado()
    {
        var orcamento = OrcamentoTests.Enviado(T0);
        orcamento.Aprovar(T0);

        var p = Pagamento.Criar(orcamento, T0);

        Assert.Equal(StatusPagamento.Pendente, p.Status);
        Assert.Equal(orcamento.ValorTotal, p.Valor);
        Assert.Equal(orcamento.Id, p.OrcamentoId);
    }

    [Fact]
    public void Orcamento_nao_aprovado_nao_gera_pagamento()
    {
        Assert.Throws<RegraDeNegocioException>(() => Pagamento.Criar(OrcamentoTests.Enviado(T0), T0));
    }

    [Fact]
    public void Registrar_cobranca_guarda_preferencia_e_link()
    {
        var p = Pendente();

        p.RegistrarCobranca("pref-1", "https://mp/checkout/pref-1");

        Assert.Equal("pref-1", p.PreferenceId);
        Assert.Equal("https://mp/checkout/pref-1", p.LinkPagamento);
    }

    [Fact]
    public void Confirmar_pendente_e_idempotente()
    {
        var p = Pendente();

        Assert.Equal(ResultadoConfirmacao.Confirmado, p.Confirmar("mp-1", T0.AddMinutes(5)));
        Assert.Equal(ResultadoConfirmacao.JaConfirmado, p.Confirmar("mp-1", T0.AddMinutes(6)));

        Assert.Equal(StatusPagamento.Aprovado, p.Status);
        Assert.Equal("mp-1", p.MpPaymentId);
        Assert.Equal(T0.AddMinutes(5), p.ConfirmadoEm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aprovacao_depois_de_recusa_ou_cancelamento_e_tardia(bool recusado)
    {
        var p = Pendente();
        if (recusado) p.Recusar("mp-0", "cartão recusado", T0);
        else p.Cancelar(T0);

        Assert.Equal(ResultadoConfirmacao.Tardio, p.Confirmar("mp-1", T0.AddMinutes(10)));
        Assert.Equal(StatusPagamento.Aprovado, p.Status);
    }

    [Fact]
    public void Recusa_so_vale_para_pendente()
    {
        var p = Pendente();

        Assert.True(p.Recusar("mp-1", null, T0));
        Assert.Equal("recusado pelo gateway", p.MotivoRecusa);
        Assert.False(p.Recusar("mp-2", "de novo", T0));
        Assert.Equal("mp-1", p.MpPaymentId);
    }

    [Fact]
    public void Cancelar_so_vale_para_pendente()
    {
        var p = Pendente();
        Assert.True(p.Cancelar(T0));
        Assert.Equal(StatusPagamento.Cancelado, p.Status);

        var aprovado = Pendente();
        aprovado.Confirmar("mp-1", T0);
        Assert.False(aprovado.Cancelar(T0));
        Assert.Equal(StatusPagamento.Aprovado, aprovado.Status);
    }

    [Fact]
    public void Estorno_so_de_aprovado_e_nao_estorna_duas_vezes()
    {
        var p = Pendente();
        p.Confirmar("mp-1", T0);

        Assert.True(p.Estornar("refund-1", T0.AddHours(1)));
        Assert.False(p.Estornar("refund-2", T0.AddHours(2)));

        Assert.Equal(StatusPagamento.Estornado, p.Status);
        Assert.Equal("refund-1", p.RefundId);
        Assert.Equal(ResultadoConfirmacao.JaEstornado, p.Confirmar("mp-1", T0.AddHours(3)));
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente)]
    [InlineData(StatusPagamento.Recusado)]
    [InlineData(StatusPagamento.Cancelado)]
    public void Estornar_pagamento_nao_aprovado_e_impossivel(StatusPagamento status)
    {
        var p = Pendente();
        if (status == StatusPagamento.Recusado) p.Recusar(null, null, T0);
        if (status == StatusPagamento.Cancelado) p.Cancelar(T0);

        Assert.Throws<EstornoImpossivelException>(() => p.Estornar("refund-1", T0));
    }
}
