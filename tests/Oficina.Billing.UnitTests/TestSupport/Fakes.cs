using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Domain;

namespace Oficina.Billing.UnitTests.TestSupport;

/// <summary>Repositório em memória: guarda as mesmas instâncias (como o change tracker do EF).</summary>
public sealed class RepositorioEmMemoria : IBillingRepositorio
{
    private readonly List<Orcamento> _orcamentos = [];
    private readonly List<Pagamento> _pagamentos = [];

    public List<Notificacao> Notificacoes { get; } = [];
    public IReadOnlyList<Orcamento> Orcamentos => _orcamentos;
    public IReadOnlyList<Pagamento> Pagamentos => _pagamentos;
    public int Gravacoes { get; private set; }

    public Task<Orcamento?> ObterOrcamentoAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orcamentos.FirstOrDefault(o => o.Id == id));

    public Task<Orcamento?> ObterOrcamentoDaSagaAsync(Guid correlationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_orcamentos.FirstOrDefault(o => o.CorrelationId == correlationId));

    public Task<IReadOnlyList<Orcamento>> ListarOrcamentosDaOsAsync(int osId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Orcamento>>(_orcamentos.Where(o => o.OsId == osId).OrderByDescending(o => o.CriadoEm).ToList());

    public Task<Pagamento?> ObterPagamentoAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pagamentos.FirstOrDefault(p => p.Id == id));

    public Task<Pagamento?> ObterPagamentoDoOrcamentoAsync(Guid orcamentoId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pagamentos.Where(p => p.OrcamentoId == orcamentoId).OrderByDescending(p => p.CriadoEm).FirstOrDefault());

    public void Adicionar(Orcamento orcamento) => _orcamentos.Add(orcamento);

    public void Adicionar(Pagamento pagamento) => _pagamentos.Add(pagamento);

    public void Adicionar(Notificacao notificacao) => Notificacoes.Add(notificacao);

    public Task SalvarAsync(CancellationToken cancellationToken = default)
    {
        Gravacoes++;
        return Task.CompletedTask;
    }
}

public sealed class PublicadorEmMemoria : IPublicadorEventos
{
    public List<object> Publicadas { get; } = [];

    public Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class
    {
        Publicadas.Add(mensagem);
        return Task.CompletedTask;
    }
}

public sealed class Relogio : TimeProvider
{
    private DateTimeOffset _agora;

    public Relogio(DateTime inicio) => _agora = new DateTimeOffset(inicio, TimeSpan.Zero);

    public DateTime Agora => _agora.UtcDateTime;

    public override DateTimeOffset GetUtcNow() => _agora;

    public DateTime Avancar(TimeSpan quanto)
    {
        _agora = _agora.Add(quanto);
        return Agora;
    }
}
