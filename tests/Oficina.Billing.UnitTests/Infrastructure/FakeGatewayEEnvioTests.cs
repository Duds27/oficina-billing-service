using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Infrastructure.Notificacoes;
using Oficina.Billing.Infrastructure.Pagamentos;

namespace Oficina.Billing.UnitTests.Infrastructure;

public class FakeGatewayEEnvioTests
{
    private readonly FakeGatewayPagamento _gateway = new();

    [Fact]
    public async Task Cobranca_tem_preferencia_e_link_ficticios()
    {
        var pagamentoId = Guid.NewGuid();

        var cobranca = await _gateway.CriarCobrancaAsync(new NovaCobranca(pagamentoId, "OS-1", 10m, null, DateTime.UtcNow));

        Assert.Equal($"fake-pref-{pagamentoId:N}", cobranca.PreferenceId);
        Assert.Equal($"https://pagamento.fake.local/checkout/{pagamentoId}", cobranca.LinkPagamento);
    }

    [Fact]
    public async Task Simulacao_vira_a_situacao_consultada()
    {
        var pagamentoId = Guid.NewGuid();
        var aprovado = _gateway.Simular(pagamentoId, 10m, aprovado: true);
        var recusado = _gateway.Simular(pagamentoId, 10m, aprovado: false);

        Assert.Equal(new SituacaoPagamento(aprovado, pagamentoId, StatusNoGateway.Aprovado, 10m, null), await _gateway.ConsultarPagamentoAsync(aprovado));
        Assert.Equal(StatusNoGateway.Recusado, (await _gateway.ConsultarPagamentoAsync(recusado))!.Status);
        Assert.Null(await _gateway.ConsultarPagamentoAsync("nao-existe"));
    }

    [Fact]
    public async Task Estorno_com_a_mesma_chave_devolve_o_mesmo_reembolso()
    {
        var mpPaymentId = _gateway.Simular(Guid.NewGuid(), 10m, aprovado: true);
        var chave = Guid.NewGuid();

        var primeiro = await _gateway.EstornarAsync(mpPaymentId, chave);
        var repetido = await _gateway.EstornarAsync(mpPaymentId, chave);
        var outro = await _gateway.EstornarAsync(mpPaymentId, Guid.NewGuid());

        Assert.Equal(primeiro, repetido);
        Assert.NotEqual(primeiro, outro);
        Assert.Equal(StatusNoGateway.Estornado, (await _gateway.ConsultarPagamentoAsync(mpPaymentId))!.Status);
    }

    [Theory]
    [InlineData("maria@example.com", "m***@example.com")]
    [InlineData("a@b.c", "a***@b.c")]
    [InlineData("sem-arroba", "***")]
    [InlineData("@inicio.com", "***")]
    public void Email_sai_mascarado_no_log(string email, string esperado)
    {
        Assert.Equal(esperado, EnvioNotificacoes.Mascarar(email));
    }
}
