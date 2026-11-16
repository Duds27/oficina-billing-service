using Oficina.Billing.Domain;

namespace Oficina.Billing.Application.Abstractions;

/// <summary>
/// Persistência do billing. As alterações ficam pendentes até <see cref="SalvarAsync"/>, que grava
/// tudo numa transação: orçamento, pagamento, notificação e as mensagens do outbox do MassTransit.
/// </summary>
public interface IBillingRepositorio
{
    Task<Orcamento?> ObterOrcamentoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>O orçamento da saga (no máximo um por <c>CorrelationId</c>).</summary>
    Task<Orcamento?> ObterOrcamentoDaSagaAsync(Guid correlationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Orcamento>> ListarOrcamentosDaOsAsync(int osId, CancellationToken cancellationToken = default);

    Task<Pagamento?> ObterPagamentoAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>O pagamento mais recente do orçamento, ou <c>null</c>.</summary>
    Task<Pagamento?> ObterPagamentoDoOrcamentoAsync(Guid orcamentoId, CancellationToken cancellationToken = default);

    void Adicionar(Orcamento orcamento);

    void Adicionar(Pagamento pagamento);

    void Adicionar(Notificacao notificacao);

    /// <summary>Grava tudo o que está pendente.</summary>
    /// <exception cref="ConflitoDeConcorrenciaException">Outra gravação mudou os mesmos registros.</exception>
    Task SalvarAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outra gravação mudou o registro depois da leitura (concorrência otimista).</summary>
public sealed class ConflitoDeConcorrenciaException : Exception
{
    public ConflitoDeConcorrenciaException(string message) : base(message)
    {
    }

    public ConflitoDeConcorrenciaException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ConflitoDeConcorrenciaException()
    {
    }
}
