using System.Collections.Concurrent;
using Oficina.Billing.Application.Abstractions;

namespace Oficina.Billing.Infrastructure.Pagamentos;

/// <summary>Simula o cliente pagando, no gateway Fake (endpoint <c>/dev/pagamentos/{id}/simular</c>).</summary>
public interface ISimuladorPagamentos
{
    /// <summary>Registra o pagamento no "gateway" e devolve o id dele, como o Mercado Pago faria.</summary>
    string Simular(Guid pagamentoId, decimal valor, bool aprovado);
}

/// <summary>
/// Gateway em memória, sem rede nem credenciais: testes, CI, docker-compose e BDD
/// (<c>Pagamentos__Gateway=Fake</c>). O link é fictício, e o resultado do pagamento é
/// decidido pelo <see cref="ISimuladorPagamentos"/>.
/// </summary>
internal sealed class FakeGatewayPagamento : IGatewayPagamento, ISimuladorPagamentos
{
    public const string MotivoRecusaSimulada = "cc_rejected_other_reason (simulado)";

    private readonly ConcurrentDictionary<string, Registro> _pagamentos = new();
    private readonly ConcurrentDictionary<Guid, string> _estornos = new();

    public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cobranca);
        return Task.FromResult(new CobrancaCriada(
            $"fake-pref-{cobranca.PagamentoId:N}",
            $"https://pagamento.fake.local/checkout/{cobranca.PagamentoId}"));
    }

    public string Simular(Guid pagamentoId, decimal valor, bool aprovado)
    {
        var mpPaymentId = $"fake-{Guid.NewGuid():N}"[..17];
        _pagamentos[mpPaymentId] = new Registro(
            pagamentoId,
            valor,
            aprovado ? StatusNoGateway.Aprovado : StatusNoGateway.Recusado,
            aprovado ? null : MotivoRecusaSimulada);
        return mpPaymentId;
    }

    public Task<SituacaoPagamento?> ConsultarPagamentoAsync(string mpPaymentId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pagamentos.TryGetValue(mpPaymentId, out var r)
            ? new SituacaoPagamento(mpPaymentId, r.PagamentoId, r.Status, r.Valor, r.Detalhe)
            : null);

    public Task<EstornoRealizado> EstornarAsync(string mpPaymentId, Guid chaveIdempotencia, CancellationToken cancellationToken = default)
    {
        // Mesma chave, mesmo estorno (como o X-Idempotency-Key do Mercado Pago).
        var refundId = _estornos.GetOrAdd(chaveIdempotencia, _ => $"fake-refund-{Guid.NewGuid():N}"[..24]);
        _pagamentos.AddOrUpdate(mpPaymentId,
            _ => new Registro(null, 0, StatusNoGateway.Estornado, null),
            (_, r) => r with { Status = StatusNoGateway.Estornado });
        return Task.FromResult(new EstornoRealizado(refundId));
    }

    private sealed record Registro(Guid? PagamentoId, decimal Valor, StatusNoGateway Status, string? Detalhe);
}
