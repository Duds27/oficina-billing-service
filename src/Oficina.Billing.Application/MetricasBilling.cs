using System.Diagnostics.Metrics;

namespace Oficina.Billing.Application;

/// <summary>
/// Métricas de negócio do billing (Meter <c>Oficina.Billing</c>), exportadas pelo OpenTelemetry.
/// Só usa System.Diagnostics.Metrics: a camada de aplicação não depende do exportador.
/// </summary>
/// <remarks>
/// As medições acontecem antes de salvar. Se a gravação falhar e a mensagem for tentada de
/// novo, o mesmo resultado pode ser contado duas vezes: desvio aceitável para métricas.
/// </remarks>
public static class MetricasBilling
{
    public const string NomeMeter = "Oficina.Billing";

    private static readonly Meter Meter = new(NomeMeter, "1.0.0");

    /// <summary>Tag <c>resultado</c>: <c>aprovado</c>, <c>recusado</c> ou <c>expirado</c> (venceu antes da decisão).</summary>
    public static readonly Counter<long> DecisaoOrcamento =
        Meter.CreateCounter<long>("orcamento.decisao", unit: "{orcamento}", description: "Decisões do cliente sobre o orçamento.");

    /// <summary>
    /// Tag <c>status</c>: <c>aprovado</c>, <c>recusado</c> ou <c>tardio</c> (aprovado depois de o
    /// orçamento ser cancelado ou o pagamento recusado; é estornado sozinho).
    /// </summary>
    public static readonly Counter<long> ResultadoPagamento =
        Meter.CreateCounter<long>("pagamento.resultado", unit: "{pagamento}", description: "Resultado dos pagamentos no gateway.");

    public static readonly Counter<long> Estorno =
        Meter.CreateCounter<long>("pagamento.estorno", unit: "{pagamento}", description: "Pagamentos estornados (compensação da saga ou automáticos).");

    public static void Decisao(string resultado) =>
        DecisaoOrcamento.Add(1, new KeyValuePair<string, object?>("resultado", resultado));

    public static void Pagamento(string status) =>
        ResultadoPagamento.Add(1, new KeyValuePair<string, object?>("status", status));
}
