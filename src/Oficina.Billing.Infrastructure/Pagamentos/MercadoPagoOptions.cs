namespace Oficina.Billing.Infrastructure.Pagamentos;

/// <summary>
/// Seção <c>Pagamentos:MercadoPago</c>. O <see cref="AccessToken"/> (de <b>teste</b>) e o
/// <see cref="WebhookSecret"/> vêm do Secrets Manager (<c>billing/mercadopago</c>) pelas variáveis
/// <c>Pagamentos__MercadoPago__AccessToken</c> e <c>Pagamentos__MercadoPago__WebhookSecret</c>.
/// Nunca no repositório nem no state do Terraform.
/// </summary>
public sealed class MercadoPagoOptions
{
    public const string Secao = "Pagamentos:MercadoPago";

    public Uri BaseUrl { get; set; } = new("https://api.mercadopago.com");

    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Segredo da assinatura <c>x-signature</c> do webhook.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>URL pública do webhook (rota <c>POST /webhooks/mercadopago</c> do API Gateway).</summary>
    public Uri? NotificationUrl { get; set; }

    /// <summary>
    /// Com credenciais de teste, o link de pagamento é o <c>sandbox_init_point</c>. Em produção,
    /// <c>init_point</c>.
    /// </summary>
    public bool UsarLinkSandbox { get; set; } = true;

    /// <summary>Tempo máximo de cada tentativa.</summary>
    public TimeSpan TimeoutPorTentativa { get; set; } = TimeSpan.FromSeconds(5);
}
