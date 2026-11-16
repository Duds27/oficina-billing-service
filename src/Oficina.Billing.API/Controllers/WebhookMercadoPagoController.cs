using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Infrastructure.Pagamentos;

namespace Oficina.Billing.API.Controllers;

/// <summary>
/// Webhook do Mercado Pago. Público (sem JWT; com throttling no API Gateway), mas só aceita
/// notificação com a assinatura <c>x-signature</c> válida. Responde rápido: o processamento
/// (consulta ao Mercado Pago e eventos da saga) é assíncrono, pelo outbox.
/// </summary>
[ApiController]
[Route("webhooks/mercadopago")]
[AllowAnonymous]
public sealed partial class WebhookMercadoPagoController : ControllerBase
{
    private readonly IValidadorWebhook _validador;
    private readonly OrcamentosUseCases _useCases;
    private readonly ILogger<WebhookMercadoPagoController> _logger;

    public WebhookMercadoPagoController(IValidadorWebhook validador, OrcamentosUseCases useCases, ILogger<WebhookMercadoPagoController> logger)
    {
        _validador = validador;
        _useCases = useCases;
        _logger = logger;
    }

    /// <summary>Notificação de pagamento (<c>?data.id=...&amp;type=payment</c>).</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receber(
        [FromQuery(Name = "data.id")] string? dataId,
        [FromQuery] string? type,
        [FromHeader(Name = "x-signature")] string? assinatura,
        [FromHeader(Name = "x-request-id")] string? requestId,
        CancellationToken cancellationToken)
    {
        if (!_validador.Valida(dataId, requestId, assinatura))
        {
            LogAssinaturaInvalida(requestId);
            return Unauthorized();
        }

        // Outros tópicos (merchant_order etc.) não interessam: confirma para o Mercado Pago não reenviar.
        if (!string.Equals(type, "payment", StringComparison.OrdinalIgnoreCase))
            return Ok();

        await _useCases.RegistrarNotificacaoAsync(dataId!, requestId, cancellationToken);
        LogRecebida(dataId!, requestId);
        return Ok();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook do Mercado Pago com assinatura inválida (request {RequestId}); recusado")]
    private partial void LogAssinaturaInvalida(string? requestId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook do Mercado Pago: pagamento {MpPaymentId} (request {RequestId}) na fila")]
    private partial void LogRecebida(string mpPaymentId, string? requestId);
}
