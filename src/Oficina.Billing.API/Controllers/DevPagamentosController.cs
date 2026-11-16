using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Infrastructure.Pagamentos;

namespace Oficina.Billing.API.Controllers;

/// <summary>
/// Só no ambiente <c>Development</c> e com o gateway Fake: simula o cliente pagando, como se o
/// webhook do Mercado Pago tivesse chegado. Não é roteado pelo API Gateway.
/// </summary>
[ApiController]
[Route("dev/pagamentos")]
[AllowAnonymous]
public sealed class DevPagamentosController : ControllerBase
{
    private readonly IWebHostEnvironment _ambiente;
    private readonly IServiceProvider _servicos;

    public DevPagamentosController(IWebHostEnvironment ambiente, IServiceProvider servicos)
    {
        _ambiente = ambiente;
        _servicos = servicos;
    }

    /// <summary>Simula o resultado do pagamento (<c>aprovado</c> ou <c>recusado</c>).</summary>
    [HttpPost("{id:guid}/simular")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Simular(Guid id, [FromQuery] string resultado, CancellationToken cancellationToken)
    {
        var simulador = _servicos.GetService<ISimuladorPagamentos>();
        if (!_ambiente.IsDevelopment() || simulador is null)
            return NotFound();

        var aprovado = resultado?.ToUpperInvariant() switch
        {
            "APROVADO" => true,
            "RECUSADO" => false,
            _ => (bool?)null
        };
        if (aprovado is null)
            return BadRequest(new { error = "resultado deve ser aprovado ou recusado." });

        var pagamento = await _servicos.GetRequiredService<IBillingRepositorio>().ObterPagamentoAsync(id, cancellationToken);
        if (pagamento is null)
            return NotFound(new { error = $"Pagamento {id} não encontrado." });

        var mpPaymentId = simulador.Simular(pagamento.Id, pagamento.Valor, aprovado.Value);
        await _servicos.GetRequiredService<OrcamentosUseCases>().RegistrarNotificacaoAsync(mpPaymentId, "simulado", cancellationToken);
        return Accepted(new { mpPaymentId });
    }
}
