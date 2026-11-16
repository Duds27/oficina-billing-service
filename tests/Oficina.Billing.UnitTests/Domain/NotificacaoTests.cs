using Oficina.Billing.Domain;

namespace Oficina.Billing.UnitTests.Domain;

public class NotificacaoTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Email_tem_o_resumo_o_total_em_reais_e_o_link_de_aprovacao()
    {
        var orcamento = OrcamentoTests.Enviado(T0);

        var n = Notificacao.DoOrcamento(orcamento, "https://oficina/orcamentos/1", T0)!;

        Assert.Equal("maria@example.com", n.Destinatario);
        Assert.Equal("Orçamento da OS OS-0042: R$ 211,90", n.Assunto);
        Assert.Contains("- Filtro de óleo: 2 x R$ 45,95 = R$ 91,90", n.Corpo, StringComparison.Ordinal);
        Assert.Contains("Válido até 18/11/2026 12:00 (UTC)", n.Corpo, StringComparison.Ordinal);
        Assert.Contains("https://oficina/orcamentos/1", n.Corpo, StringComparison.Ordinal);
        Assert.Null(n.EnviadaEm);
    }

    [Fact]
    public void Cliente_sem_email_nao_gera_notificacao()
    {
        var orcamento = Orcamento.Gerar(Guid.NewGuid(), 1, "OS-1", new ClienteOrcamento(1, "João", null), OrcamentoTests.Itens(), TimeSpan.FromHours(1), T0);

        Assert.Null(Notificacao.DoOrcamento(orcamento, "x", T0));
    }

    [Fact]
    public void Marcar_enviada_guarda_a_primeira_data()
    {
        var n = Notificacao.DoOrcamento(OrcamentoTests.Enviado(T0), "x", T0)!;

        n.MarcarEnviada(T0.AddMinutes(1));
        n.MarcarEnviada(T0.AddMinutes(2));

        Assert.Equal(T0.AddMinutes(1), n.EnviadaEm);
    }
}
