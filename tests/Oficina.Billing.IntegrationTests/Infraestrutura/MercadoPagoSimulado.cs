using System.Globalization;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>
/// A API do Mercado Pago no WireMock, com os formatos da documentação oficial (preferência,
/// pagamento e estorno). Os payloads reais do sandbox entram quando o spike S3 for feito.
/// </summary>
public sealed class MercadoPagoSimulado : IDisposable
{
    public const string Token = "TEST-token-falso-dos-testes";
    public const string SegredoWebhook = "segredo-de-teste-do-webhook";

    public MercadoPagoSimulado()
    {
        Servidor = WireMockServer.Start();
        Servidor.Given(Request.Create().WithPath("/checkout/preferences").UsingPost().WithHeader("Authorization", $"Bearer {Token}"))
            .RespondWith(Json(201, """
                {"id":"123456789-pref","init_point":"https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123456789-pref",
                 "sandbox_init_point":"https://sandbox.mercadopago.com.br/checkout/v1/redirect?pref_id=123456789-pref"}
                """));
    }

    public WireMockServer Servidor { get; }

    public string Url => Servidor.Url! + "/";

    /// <summary>Configura a API do billing para usar este servidor.</summary>
    public void Configurar(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factory.Configuracoes["Pagamentos:Gateway"] = "MercadoPago";
        factory.Configuracoes["Pagamentos:MercadoPago:BaseUrl"] = Url;
        factory.Configuracoes["Pagamentos:MercadoPago:AccessToken"] = Token;
        factory.Configuracoes["Pagamentos:MercadoPago:WebhookSecret"] = SegredoWebhook;
        factory.Configuracoes["Pagamentos:MercadoPago:NotificationUrl"] = "https://api.oficina.teste/webhooks/mercadopago";
        // Folga para o primeiro acesso ao WireMock (aquecimento) não estourar o timeout.
        factory.Configuracoes["Pagamentos:MercadoPago:TimeoutPorTentativa"] = "00:00:10";
    }

    /// <summary><c>GET /v1/payments/{id}</c> com o status pedido e o nosso id no <c>external_reference</c>.</summary>
    public void Pagamento(string mpPaymentId, Guid pagamentoId, string status, decimal valor, string statusDetail = "accredited") =>
        Servidor.Given(Request.Create().WithPath($"/v1/payments/{mpPaymentId}").UsingGet())
            .RespondWith(Json(200, CorpoPagamento(mpPaymentId, pagamentoId, status, valor, statusDetail)));

    /// <summary>A primeira consulta falha (503); a segunda responde aprovado.</summary>
    public void PagamentoComFalhaTemporaria(string mpPaymentId, Guid pagamentoId, decimal valor)
    {
        Servidor.Given(Request.Create().WithPath($"/v1/payments/{mpPaymentId}").UsingGet())
            .InScenario($"instavel-{mpPaymentId}").WillSetStateTo("recuperado")
            .RespondWith(Json(503, """{"message":"Service Unavailable"}"""));
        Servidor.Given(Request.Create().WithPath($"/v1/payments/{mpPaymentId}").UsingGet())
            .InScenario($"instavel-{mpPaymentId}").WhenStateIs("recuperado")
            .RespondWith(Json(200, CorpoPagamento(mpPaymentId, pagamentoId, "approved", valor, "accredited")));
    }

    /// <summary><c>POST /v1/payments/{id}/refunds</c> → estorno total.</summary>
    public void Estorno(string mpPaymentId, long refundId) =>
        Servidor.Given(Request.Create().WithPath($"/v1/payments/{mpPaymentId}/refunds").UsingPost())
            .RespondWith(Json(201, $$"""{"id":{{refundId}},"payment_id":{{mpPaymentId}},"amount":0,"status":"approved"}"""));

    public IReadOnlyList<(string Metodo, string Caminho, string? Idempotencia, string? Corpo)> Chamadas(string caminho) =>
        Servidor.LogEntries
            .Where(e => e.RequestMessage.Path == caminho)
            .Select(e => (
                e.RequestMessage.Method,
                e.RequestMessage.Path,
                e.RequestMessage.Headers is { } h && h.TryGetValue("X-Idempotency-Key", out var v) ? v.FirstOrDefault() : null,
                e.RequestMessage.Body))
            .ToList();

    public void Dispose()
    {
        Servidor.Stop();
        Servidor.Dispose();
    }

    private static string CorpoPagamento(string mpPaymentId, Guid pagamentoId, string status, decimal valor, string statusDetail) => $$"""
        {"id":{{mpPaymentId}},"status":"{{status}}","status_detail":"{{statusDetail}}",
         "external_reference":"{{pagamentoId}}","transaction_amount":{{valor.ToString(CultureInfo.InvariantCulture)}},
         "currency_id":"BRL","payment_method_id":"master","payment_type_id":"credit_card"}
        """;

    private static IResponseBuilder Json(int status, string corpo) =>
        Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(corpo);
}
