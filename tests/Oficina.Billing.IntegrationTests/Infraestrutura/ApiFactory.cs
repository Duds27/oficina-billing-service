using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>A API num banco PostgreSQL só dela; as migrations rodam na inicialização.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveJwt = "OficinaMecanicaSecretKeyMustBe32CharactersMin!";

    public ApiFactory(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    /// <summary>Configurações extras do teste (chave no formato <c>Secao:Chave</c>).</summary>
    public Dictionary<string, string> Configuracoes { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:Key", ChaveJwt);
        builder.UseSetting("ConnectionStrings:BillingDb", ConnectionString);
        foreach (var (chave, valor) in Configuracoes)
            builder.UseSetting(chave, valor);
    }
}
