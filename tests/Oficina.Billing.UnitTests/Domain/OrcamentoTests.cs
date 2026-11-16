using Oficina.Billing.Domain;

namespace Oficina.Billing.UnitTests.Domain;

public class OrcamentoTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Correlacao = Guid.Parse("3f2c9a4e-8d1b-4c55-9a7e-2b6f0d9e1a11");
    private static readonly ClienteOrcamento Maria = new(7, "Maria Souza", "maria@example.com");

    internal static ItemOrcamento[] Itens() =>
    [
        new("Servico", 3, "Troca de óleo", 1, 120.00m),
        new("Peca", 15, "Filtro de óleo", 2, 45.95m)
    ];

    internal static Orcamento Enviado(DateTime? agora = null)
    {
        var o = Orcamento.Gerar(Correlacao, 42, "OS-0042", Maria, Itens(), TimeSpan.FromHours(48), agora ?? T0);
        o.MarcarEnviado(agora ?? T0);
        return o;
    }

    [Fact]
    public void Gerar_calcula_o_total_pelos_itens()
    {
        var o = Orcamento.Gerar(Correlacao, 42, "OS-0042", Maria, Itens(), TimeSpan.FromHours(48), T0);

        Assert.Equal(211.90m, o.ValorTotal);
        Assert.Equal(StatusOrcamento.Gerado, o.Status);
        Assert.Equal(T0.AddHours(48), o.ValidoAte);
        Assert.Equal(2, o.Itens.Count);
        Assert.Equal(91.90m, o.Itens[1].Subtotal);
        Assert.NotEqual(Guid.Empty, o.Id);
    }

    [Fact]
    public void Subtotal_arredonda_para_centavos()
    {
        var item = new ItemOrcamento("Servico", 1, "Mão de obra", 1.333m, 10m);
        Assert.Equal(13.33m, item.Subtotal);
    }

    [Fact]
    public void Gerar_exige_itens_validos()
    {
        Assert.Throws<ArgumentException>(() => Orcamento.Gerar(Correlacao, 42, "OS-0042", Maria, [], TimeSpan.FromHours(1), T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemOrcamento("Peca", 1, "x", 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemOrcamento("Peca", 1, "x", 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Orcamento.Gerar(Correlacao, 0, "OS-0042", Maria, Itens(), TimeSpan.FromHours(1), T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Orcamento.Gerar(Correlacao, 42, "OS-0042", Maria, Itens(), TimeSpan.Zero, T0));
    }

    [Fact]
    public void So_envia_orcamento_gerado()
    {
        var o = Enviado();

        Assert.Equal(StatusOrcamento.Enviado, o.Status);
        Assert.Equal(T0, o.EnviadoEm);
        Assert.Throws<RegraDeNegocioException>(() => o.MarcarEnviado(T0));
    }

    [Fact]
    public void Aprovar_dentro_da_validade()
    {
        var o = Enviado();

        o.Aprovar(T0.AddHours(47));

        Assert.Equal(StatusOrcamento.Aprovado, o.Status);
        Assert.Equal(T0.AddHours(47), o.DecididoEm);
    }

    [Fact]
    public void Recusar_guarda_o_motivo()
    {
        var o = Enviado();

        o.Recusar("  caro demais  ", T0.AddHours(1));

        Assert.Equal(StatusOrcamento.Recusado, o.Status);
        Assert.Equal("caro demais", o.MotivoRecusa);
    }

    [Fact]
    public void Nao_aprova_nem_recusa_vencido()
    {
        var o = Enviado();

        var erro = Assert.Throws<RegraDeNegocioException>(() => o.Aprovar(T0.AddHours(49)));
        Assert.Contains("venceu", erro.Message, StringComparison.Ordinal);
        Assert.Throws<RegraDeNegocioException>(() => o.Recusar(null, T0.AddHours(49)));
        Assert.Equal(StatusOrcamento.Enviado, o.Status);
    }

    [Theory]
    [InlineData(StatusOrcamento.Aprovado)]
    [InlineData(StatusOrcamento.Recusado)]
    [InlineData(StatusOrcamento.Cancelado)]
    public void So_decide_orcamento_enviado(StatusOrcamento status)
    {
        var o = Enviado();
        if (status == StatusOrcamento.Aprovado) o.Aprovar(T0);
        if (status == StatusOrcamento.Recusado) o.Recusar(null, T0);
        if (status == StatusOrcamento.Cancelado) o.Cancelar(null, T0);

        Assert.Throws<RegraDeNegocioException>(() => o.Aprovar(T0));
        Assert.Throws<RegraDeNegocioException>(() => o.Recusar(null, T0));
    }

    [Fact]
    public void Expirar_so_afeta_o_enviado_vencido()
    {
        var o = Enviado();

        Assert.False(o.ExpirarSeVencido(T0.AddHours(48)));
        Assert.True(o.ExpirarSeVencido(T0.AddHours(48).AddSeconds(1)));
        Assert.Equal(StatusOrcamento.Expirado, o.Status);
        Assert.False(o.ExpirarSeVencido(T0.AddHours(100)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancelar_vale_para_enviado_e_aprovado(bool aprovado)
    {
        var o = Enviado();
        if (aprovado) o.Aprovar(T0);

        Assert.True(o.Cancelar("Prazo de pagamento expirado", T0.AddHours(2)));

        Assert.Equal(StatusOrcamento.Cancelado, o.Status);
        Assert.Equal("Prazo de pagamento expirado", o.MotivoCancelamento);
        Assert.Equal(T0.AddHours(2), o.CanceladoEm);
    }

    [Fact]
    public void Cancelar_e_idempotente_e_nao_mexe_em_recusado()
    {
        var cancelado = Enviado();
        cancelado.Cancelar(null, T0);
        var recusado = Enviado();
        recusado.Recusar("não", T0);

        Assert.False(cancelado.Cancelar("de novo", T0.AddHours(1)));
        Assert.Equal("sem motivo informado", cancelado.MotivoCancelamento);
        Assert.False(recusado.Cancelar("tarde", T0));
        Assert.Equal(StatusOrcamento.Recusado, recusado.Status);
    }

    [Fact]
    public void Cancelamento_antecipado_fica_registrado_sem_itens()
    {
        var o = Orcamento.RegistrarCancelamentoAntecipado(Correlacao, 42, "OS-0042", "Cancelada pelo atendente", T0);

        Assert.Equal(StatusOrcamento.Cancelado, o.Status);
        Assert.Empty(o.Itens);
        Assert.Equal(0m, o.ValorTotal);
        Assert.Equal("Cancelada pelo atendente", o.MotivoCancelamento);
    }
}
