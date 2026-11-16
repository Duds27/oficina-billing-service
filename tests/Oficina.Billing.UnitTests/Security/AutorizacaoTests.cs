using System.Security.Claims;
using Oficina.Billing.API.Security;
using Oficina.Billing.Application.UseCases;

namespace Oficina.Billing.UnitTests.Security;

public class AutorizacaoTests
{
    private static ClaimsPrincipal Usuario(params Claim[] claims) => new(new ClaimsIdentity(claims, "teste"));

    [Fact]
    public void Cliente_da_Lambda_vem_com_o_clienteId()
    {
        var quem = Usuario(new Claim(ClaimTypes.Role, "Cliente"), new Claim("clienteId", "7")).Solicitante();

        Assert.Equal(new Solicitante(false, false, 7), quem);
        Assert.True(quem.PodeDecidir(7));
        Assert.False(quem.PodeDecidir(8));
    }

    [Fact]
    public void Token_antigo_de_cliente_usa_o_sub()
    {
        Assert.Equal(7, Usuario(new Claim(ClaimTypes.Role, "Cliente"), new Claim(ClaimTypes.NameIdentifier, "7")).Solicitante().ClienteId);
    }

    [Fact]
    public void Atendente_ve_e_decide_mecanico_so_ve()
    {
        var atendente = Usuario(new Claim(ClaimTypes.Role, "Admin")).Solicitante();
        var mecanico = Usuario(new Claim(ClaimTypes.Role, "Mecanico")).Solicitante();

        Assert.True(atendente.PodeVer(7) && atendente.PodeDecidir(7));
        Assert.True(mecanico.PodeVer(7));
        Assert.False(mecanico.PodeDecidir(7));
    }

    [Fact]
    public void Sem_papel_conhecido_nao_ve_nada()
    {
        var quem = Usuario(new Claim("clienteId", "7")).Solicitante();

        Assert.Null(quem.ClienteId);
        Assert.False(quem.PodeVer(7));
    }
}
