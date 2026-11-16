using Microsoft.EntityFrameworkCore;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Domain;

namespace Oficina.Billing.Infrastructure.Persistence;

internal sealed class BillingRepositorio : IBillingRepositorio
{
    private readonly BillingDbContext _db;

    public BillingRepositorio(BillingDbContext db) => _db = db;

    public Task<Orcamento?> ObterOrcamentoAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Orcamentos.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Orcamento?> ObterOrcamentoDaSagaAsync(Guid correlationId, CancellationToken cancellationToken = default) =>
        _db.Orcamentos.FirstOrDefaultAsync(o => o.CorrelationId == correlationId, cancellationToken);

    public async Task<IReadOnlyList<Orcamento>> ListarOrcamentosDaOsAsync(int osId, CancellationToken cancellationToken = default) =>
        await _db.Orcamentos.Where(o => o.OsId == osId).OrderByDescending(o => o.CriadoEm).ToListAsync(cancellationToken);

    public Task<Pagamento?> ObterPagamentoAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Pagamentos.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Pagamento?> ObterPagamentoDoOrcamentoAsync(Guid orcamentoId, CancellationToken cancellationToken = default) =>
        _db.Pagamentos.Where(p => p.OrcamentoId == orcamentoId).OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(cancellationToken);

    public void Adicionar(Orcamento orcamento) => _db.Orcamentos.Add(orcamento);

    public void Adicionar(Pagamento pagamento) => _db.Pagamentos.Add(pagamento);

    public void Adicionar(Notificacao notificacao) => _db.Notificacoes.Add(notificacao);

    public async Task SalvarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflitoDeConcorrenciaException("O registro mudou desde a leitura; leia de novo e repita a operação.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Duas mensagens criando o mesmo orçamento ao mesmo tempo: quem perdeu relê e encontra o do outro.
            throw new ConflitoDeConcorrenciaException("Outro registro com a mesma chave foi gravado ao mesmo tempo.", ex);
        }
    }
}
