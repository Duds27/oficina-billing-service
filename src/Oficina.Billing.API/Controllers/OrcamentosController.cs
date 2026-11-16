using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.API.Security;
using Oficina.Billing.Application.DTOs;
using Oficina.Billing.Application.UseCases;

namespace Oficina.Billing.API.Controllers;

/// <summary>Corpo de <c>POST /api/orcamentos/{id}/aprovacao</c>.</summary>
public sealed record DecisaoOrcamentoRequest([Required] bool? Aprovado, [MaxLength(500)] string? Motivo);

/// <summary>
/// Orçamentos. A equipe da oficina vê todos; o cliente (login por CPF) só vê e decide os dele.
/// O prefixo <c>/api/orcamentos</c> é exclusivo deste serviço (roteamento por caminho no API Gateway).
/// </summary>
[ApiController]
[Route("api/orcamentos")]
[Authorize]
[Produces("application/json")]
public sealed class OrcamentosController : ControllerBase
{
    private readonly OrcamentosUseCases _useCases;

    public OrcamentosController(OrcamentosUseCases useCases) => _useCases = useCases;

    /// <summary>Orçamento com itens, status e o pagamento, se houver.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrcamentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrcamentoDto>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _useCases.ObterAsync(id, User.Solicitante(), cancellationToken);
        return resultado.Status == StatusResultado.Ok ? Ok(resultado.Valor) : this.Erro(resultado);
    }

    /// <summary>Orçamentos de uma OS (o cliente só recebe os dele).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<OrcamentoDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrcamentoDto>>> ListarDaOs([FromQuery, Required] int osId, CancellationToken cancellationToken) =>
        Ok(await _useCases.ListarDaOsAsync(osId, User.Solicitante(), cancellationToken));

    /// <summary>
    /// Aprovação ou recusa pelo cliente dono do orçamento (ou pelo atendente, registrando a decisão
    /// dele). Aprovando, cria o pagamento e devolve o link de pagamento.
    /// </summary>
    [HttpPost("{id:guid}/aprovacao")]
    [ProducesResponseType(typeof(OrcamentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrcamentoDto>> Decidir(Guid id, [FromBody] DecisaoOrcamentoRequest corpo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpo);
        var resultado = await _useCases.DecidirAsync(id, corpo.Aprovado!.Value, corpo.Motivo, User.Solicitante(), cancellationToken);
        return resultado.Status == StatusResultado.Ok ? Ok(resultado.Valor) : this.Erro(resultado);
    }
}

/// <summary>Pagamentos.</summary>
[ApiController]
[Route("api/pagamentos")]
[Authorize]
[Produces("application/json")]
public sealed class PagamentosController : ControllerBase
{
    private readonly OrcamentosUseCases _useCases;

    public PagamentosController(OrcamentosUseCases useCases) => _useCases = useCases;

    /// <summary>Status do pagamento.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PagamentoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagamentoDto>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _useCases.ObterPagamentoAsync(id, User.Solicitante(), cancellationToken);
        return resultado.Status == StatusResultado.Ok ? Ok(resultado.Valor) : this.Erro(resultado);
    }
}
