namespace Oficina.Billing.Application.Abstractions;

/// <summary>
/// Publicação de eventos. Sai pelo outbox do MassTransit: grava junto com o
/// <see cref="IBillingRepositorio.SalvarAsync"/>. Publique antes de salvar.
/// </summary>
public interface IPublicadorEventos
{
    Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class;
}

/// <summary>Gateway de pagamento (Mercado Pago no sandbox, ou o Fake nos testes e no compose).</summary>
public interface IGatewayPagamento
{
    /// <summary>Cria a cobrança (preferência do Checkout Pro). Idempotente pelo id do pagamento.</summary>
    Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default);

    /// <summary>Situação atual do pagamento no gateway (fonte da verdade). <c>null</c> se não existir.</summary>
    Task<SituacaoPagamento?> ConsultarPagamentoAsync(string mpPaymentId, CancellationToken cancellationToken = default);

    /// <summary>Estorno total. A chave de idempotência evita estornar duas vezes numa nova tentativa.</summary>
    Task<EstornoRealizado> EstornarAsync(string mpPaymentId, Guid chaveIdempotencia, CancellationToken cancellationToken = default);
}

public sealed record NovaCobranca(Guid PagamentoId, string NumeroOS, decimal Valor, string? EmailPagador, DateTime ExpiraEm);

public sealed record CobrancaCriada(string PreferenceId, string LinkPagamento);

public enum StatusNoGateway
{
    Pendente,
    Aprovado,
    Recusado,
    Estornado
}

/// <param name="MpPaymentId">Id do pagamento no gateway.</param>
/// <param name="PagamentoId">O <c>external_reference</c>: id do nosso pagamento. <c>null</c> se não for nosso.</param>
/// <param name="Status">Situação traduzida para o nosso vocabulário.</param>
/// <param name="Valor">Valor pago.</param>
/// <param name="Detalhe">Detalhe do gateway (ex.: motivo da recusa).</param>
public sealed record SituacaoPagamento(string MpPaymentId, Guid? PagamentoId, StatusNoGateway Status, decimal Valor, string? Detalhe);

public sealed record EstornoRealizado(string RefundId);

/// <summary>O gateway não respondeu ou respondeu com erro. Transitório: vale tentar de novo.</summary>
public sealed class GatewayIndisponivelException : Exception
{
    public GatewayIndisponivelException(string message) : base(message)
    {
    }

    public GatewayIndisponivelException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public GatewayIndisponivelException()
    {
    }
}
