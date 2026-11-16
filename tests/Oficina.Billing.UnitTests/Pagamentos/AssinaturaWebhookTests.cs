using Oficina.Billing.Infrastructure.Pagamentos;

namespace Oficina.Billing.UnitTests.Pagamentos;

public class AssinaturaWebhookTests
{
    private const string Segredo = "segredo-de-teste-do-webhook";
    private const string RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e";
    private const string Ts = "1742505638683";

    private static string Header(string dataId, string ts = Ts, string requestId = RequestId, string segredo = Segredo) =>
        $"ts={ts},v1={AssinaturaWebhook.Assinar(segredo, AssinaturaWebhook.Manifesto(dataId, requestId, ts))}";

    [Fact]
    public void Manifesto_segue_o_formato_do_Mercado_Pago()
    {
        Assert.Equal($"id:123456;request-id:{RequestId};ts:{Ts};", AssinaturaWebhook.Manifesto("123456", RequestId, Ts));
    }

    [Fact]
    public void Assinatura_correta_e_aceita()
    {
        Assert.True(AssinaturaWebhook.Valida(Segredo, "123456", RequestId, Header("123456")));
    }

    [Fact]
    public void Aceita_espacos_e_hex_em_maiusculas()
    {
        var v1 = AssinaturaWebhook.Assinar(Segredo, AssinaturaWebhook.Manifesto("123456", RequestId, Ts)).ToUpperInvariant();

        Assert.True(AssinaturaWebhook.Valida(Segredo, "123456", RequestId, $" ts={Ts} , v1={v1} "));
    }

    [Fact]
    public void Id_alfanumerico_entra_em_minusculas()
    {
        Assert.True(AssinaturaWebhook.Valida(Segredo, "ABC123", RequestId, Header("abc123")));
    }

    [Fact]
    public void Segredo_errado_e_recusado()
    {
        Assert.False(AssinaturaWebhook.Valida(Segredo, "123456", RequestId, Header("123456", segredo: "outro-segredo")));
    }

    [Fact]
    public void Ts_adulterado_e_recusado()
    {
        var assinatura = Header("123456");

        Assert.False(AssinaturaWebhook.Valida(Segredo, "123456", RequestId, assinatura.Replace($"ts={Ts}", "ts=1742505999999", StringComparison.Ordinal)));
    }

    [Fact]
    public void Outro_pagamento_ou_outro_request_id_sao_recusados()
    {
        var assinatura = Header("123456");

        Assert.False(AssinaturaWebhook.Valida(Segredo, "999999", RequestId, assinatura));
        Assert.False(AssinaturaWebhook.Valida(Segredo, "123456", "outro-request", assinatura));
    }

    [Theory]
    [InlineData("", "123456", RequestId, "ts=1,v1=aa")]
    [InlineData(Segredo, null, RequestId, "ts=1,v1=aa")]
    [InlineData(Segredo, "123456", null, "ts=1,v1=aa")]
    [InlineData(Segredo, "123456", RequestId, null)]
    [InlineData(Segredo, "123456", RequestId, "v1=aa")]
    [InlineData(Segredo, "123456", RequestId, "ts=1")]
    [InlineData(Segredo, "123456", RequestId, "lixo")]
    public void Falta_de_dado_e_recusada(string segredo, string? dataId, string? requestId, string? xSignature)
    {
        Assert.False(AssinaturaWebhook.Valida(segredo, dataId, requestId, xSignature));
    }
}
