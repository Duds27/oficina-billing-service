using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Oficina.Billing.Application.UseCases;

namespace Oficina.Billing.API.Security;

public static class Autorizacao
{
    public const string PapelAtendente = "Admin";
    public const string PapelMecanico = "Mecanico";
    public const string PapelCliente = "Cliente";

    /// <summary>
    /// Lê o solicitante do token. O <c>clienteId</c> vem do claim emitido pela Lambda no login por
    /// CPF (tokens antigos só têm o <c>sub</c>, com o mesmo valor).
    /// </summary>
    public static Solicitante Solicitante(this ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        var atendente = usuario.IsInRole(PapelAtendente);
        var daOficina = atendente || usuario.IsInRole(PapelMecanico);
        int? clienteId = null;
        if (usuario.IsInRole(PapelCliente))
        {
            var valor = usuario.FindFirst("clienteId")?.Value
                ?? usuario.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? usuario.FindFirst("sub")?.Value;
            if (int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                clienteId = id;
        }

        return new Solicitante(daOficina, atendente, clienteId);
    }

    /// <summary>Resposta HTTP de um resultado que não deu certo, no formato <c>{ error }</c> dos outros serviços.</summary>
    public static ObjectResult Erro<T>(this ControllerBase controller, Resultado<T> resultado)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resultado);

        return controller.StatusCode(resultado.Status switch
        {
            StatusResultado.NaoEncontrado => StatusCodes.Status404NotFound,
            StatusResultado.Proibido => StatusCodes.Status403Forbidden,
            StatusResultado.Conflito => StatusCodes.Status409Conflict,
            StatusResultado.Indisponivel => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        }, new { error = resultado.Erro });
    }
}
