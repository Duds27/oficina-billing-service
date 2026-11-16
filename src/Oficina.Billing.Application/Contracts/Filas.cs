using MassTransit;

namespace Oficina.Contracts;

/// <summary>
/// Filas deste serviço (docs/contratos-mensagens.md, seção 3). As de comando têm o nome que a
/// saga do os-service usa no <c>Send</c>.
/// </summary>
public static class FilasBilling
{
    public const string GerarOrcamento = "billing-gerar-orcamento";
    public const string CancelarOrcamento = "billing-cancelar-orcamento";
    public const string EstornarPagamento = "billing-estornar-pagamento";
    public const string ProcessarNotificacaoPagamento = "billing-processar-notificacao-pagamento";

    /// <summary>Recebe as falhas definitivas de estorno, para o alerta crítico.</summary>
    public const string FalhasDeEstorno = "billing-falhas-de-estorno";
}

/// <summary>
/// Evento interno do billing (fora do catálogo): o webhook do Mercado Pago chegou e foi validado.
/// O processamento é assíncrono para o webhook responder rápido.
/// </summary>
[MessageUrn("oficina:billing-processar-notificacao-pagamento:v1"), EntityName("oficina.billing-processar-notificacao-pagamento.v1")]
public record ProcessarNotificacaoPagamento
{
    /// <summary>Id do pagamento no Mercado Pago (<c>data.id</c> da notificação).</summary>
    public string MpPaymentId { get; init; } = string.Empty;

    /// <summary><c>x-request-id</c> da notificação, para rastrear nos logs.</summary>
    public string? RequestId { get; init; }

    public DateTime RecebidaEm { get; init; }
}
