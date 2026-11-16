using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Oficina.Billing.Infrastructure.Pagamentos;

/// <summary>Valida a assinatura do webhook do Mercado Pago.</summary>
public interface IValidadorWebhook
{
    bool Valida(string? dataId, string? requestId, string? xSignature);
}

/// <summary>
/// Header <c>x-signature: ts=...,v1=...</c>: <c>v1</c> é o HMAC-SHA256 (hex) do manifesto
/// <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c> com o segredo do webhook. Sem segredo
/// configurado, nenhuma notificação é aceita.
/// </summary>
internal sealed class AssinaturaWebhook : IValidadorWebhook
{
    private readonly IOptionsMonitor<MercadoPagoOptions> _opcoes;

    public AssinaturaWebhook(IOptionsMonitor<MercadoPagoOptions> opcoes) => _opcoes = opcoes;

    public bool Valida(string? dataId, string? requestId, string? xSignature) =>
        Valida(_opcoes.CurrentValue.WebhookSecret, dataId, requestId, xSignature);

    internal static bool Valida(string segredo, string? dataId, string? requestId, string? xSignature)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrWhiteSpace(dataId) || string.IsNullOrWhiteSpace(requestId)
            || string.IsNullOrWhiteSpace(xSignature))
            return false;

        string? ts = null, v1 = null;
        foreach (var parte in xSignature.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var igual = parte.IndexOf('=', StringComparison.Ordinal);
            if (igual <= 0)
                continue;
            var chave = parte[..igual].Trim();
            var valor = parte[(igual + 1)..].Trim();
            if (chave == "ts") ts = valor;
            else if (chave == "v1") v1 = valor;
        }

        if (string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(v1))
            return false;

        var esperado = Assinar(segredo, Manifesto(dataId, requestId, ts));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperado), Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }

    /// <summary>O <c>data.id</c> alfanumérico entra em minúsculas no manifesto (documentação do Mercado Pago).</summary>
    internal static string Manifesto(string dataId, string requestId, string ts) =>
        $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";

    internal static string Assinar(string segredo, string manifesto) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto))).ToLowerInvariant();
}
