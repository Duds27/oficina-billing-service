using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>Tokens no formato dos emissores reais (os-service e Lambda), com a chave de teste.</summary>
public static class Tokens
{
    /// <summary>Login do atendente/mecânico no os-service: claims curtas <c>unique_name</c> e <c>role</c>.</summary>
    public static string DaOficina(string papel = "Admin") =>
        Criar(new Dictionary<string, object> { ["unique_name"] = "joao", ["role"] = papel });

    /// <summary>Login por CPF na Lambda: papel <c>Cliente</c> e o claim <c>clienteId</c>.</summary>
    public static string DeCliente(int clienteId) =>
        Criar(new Dictionary<string, object>
        {
            ["sub"] = clienteId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["clienteId"] = clienteId,
            ["name"] = "Maria",
            ["role"] = "Cliente",
            ["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] = "Cliente"
        });

    public static HttpClient Autenticado(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Criar(Dictionary<string, object> claims) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "OficinaMecanicaBackend",
        Audience = "OficinaMecanicaBackend",
        Expires = DateTime.UtcNow.AddHours(1),
        Claims = claims,
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.ASCII.GetBytes(ApiFactory.ChaveJwt)), SecurityAlgorithms.HmacSha256)
    });
}
