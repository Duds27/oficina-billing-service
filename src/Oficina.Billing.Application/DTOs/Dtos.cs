using Oficina.Billing.Domain;

namespace Oficina.Billing.Application.DTOs;

public sealed record ItemOrcamentoDto(string Tipo, int ReferenciaId, string Descricao, decimal Quantidade, decimal PrecoUnitario, decimal Subtotal);

public sealed record ClienteDto(int Id, string Nome);

public sealed record PagamentoResumoDto(Guid Id, string Status, decimal Valor, string? LinkPagamento);

public sealed record OrcamentoDto(
    Guid Id,
    int OsId,
    string NumeroOS,
    string Status,
    decimal ValorTotal,
    DateTime ValidoAte,
    ClienteDto Cliente,
    IReadOnlyList<ItemOrcamentoDto> Itens,
    DateTime CriadoEm,
    DateTime? DecididoEm,
    string? MotivoRecusa,
    string? MotivoCancelamento,
    PagamentoResumoDto? Pagamento);

public sealed record PagamentoDto(
    Guid Id,
    Guid OrcamentoId,
    string Status,
    decimal Valor,
    string? LinkPagamento,
    string? MpPaymentId,
    string? MotivoRecusa,
    DateTime CriadoEm,
    DateTime? ConfirmadoEm,
    DateTime? RecusadoEm,
    DateTime? EstornadoEm);

public static class MapeamentoDtos
{
    public static OrcamentoDto ParaDto(this Orcamento o, Pagamento? pagamento)
    {
        ArgumentNullException.ThrowIfNull(o);
        return new OrcamentoDto(
            o.Id,
            o.OsId,
            o.NumeroOS,
            o.Status.ToString(),
            o.ValorTotal,
            o.ValidoAte,
            new ClienteDto(o.Cliente.Id, o.Cliente.Nome),
            o.Itens.Select(i => new ItemOrcamentoDto(i.Tipo, i.ReferenciaId, i.Descricao, i.Quantidade, i.PrecoUnitario, i.Subtotal)).ToList(),
            o.CriadoEm,
            o.DecididoEm,
            o.MotivoRecusa,
            o.MotivoCancelamento,
            pagamento is null ? null : new PagamentoResumoDto(pagamento.Id, pagamento.Status.ToString(), pagamento.Valor, pagamento.LinkPagamento));
    }

    public static PagamentoDto ParaDto(this Pagamento p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new PagamentoDto(p.Id, p.OrcamentoId, p.Status.ToString(), p.Valor, p.LinkPagamento, p.MpPaymentId,
            p.MotivoRecusa, p.CriadoEm, p.ConfirmadoEm, p.RecusadoEm, p.EstornadoEm);
    }
}
