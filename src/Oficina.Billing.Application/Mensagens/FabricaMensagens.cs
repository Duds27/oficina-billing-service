using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.Application.Mensagens;

/// <summary>Eventos que o billing publica, com o envelope preenchido a partir do orçamento.</summary>
public static class FabricaMensagens
{
    public static OrcamentoGerado OrcamentoGerado(Orcamento o, DateTime agora) =>
        Envelope(new OrcamentoGerado { OrcamentoId = o.Id, ValorTotal = o.ValorTotal, ValidoAte = o.ValidoAte }, o, agora);

    public static OrcamentoAprovado OrcamentoAprovado(Orcamento o, Pagamento p, DateTime agora) =>
        Envelope(new OrcamentoAprovado { OrcamentoId = o.Id, PagamentoId = p.Id, LinkPagamento = p.LinkPagamento ?? string.Empty }, o, agora);

    public static OrcamentoRecusado OrcamentoRecusado(Orcamento o, DateTime agora) =>
        Envelope(new OrcamentoRecusado { OrcamentoId = o.Id, Motivo = o.MotivoRecusa }, o, agora);

    public static PagamentoConfirmado PagamentoConfirmado(Orcamento o, Pagamento p, DateTime agora) =>
        Envelope(new PagamentoConfirmado { PagamentoId = p.Id, MpPaymentId = p.MpPaymentId ?? string.Empty, Valor = p.Valor }, o, agora);

    public static PagamentoRecusado PagamentoRecusado(Orcamento o, Pagamento p, DateTime agora) =>
        Envelope(new PagamentoRecusado { PagamentoId = p.Id, Motivo = p.MotivoRecusa ?? string.Empty }, o, agora);

    public static PagamentoEstornado PagamentoEstornado(Orcamento o, Pagamento p, DateTime agora) =>
        Envelope(new PagamentoEstornado { PagamentoId = p.Id, Valor = p.Valor }, o, agora);

    private static T Envelope<T>(T mensagem, Orcamento o, DateTime agora) where T : MensagemOS =>
        mensagem with { CorrelationId = o.CorrelationId, OsId = o.OsId, NumeroOS = o.NumeroOS, OcorridoEm = agora };
}
