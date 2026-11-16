namespace Oficina.Billing.Domain;

/// <summary>
/// <c>Pendente → Aprovado | Recusado | Cancelado</c>, e <c>Aprovado → Estornado</c>.
/// <c>Cancelado</c> é o pagamento pendente de um orçamento cancelado (regra 2 da seção 5 dos contratos).
/// </summary>
public enum StatusPagamento
{
    Pendente,
    Aprovado,
    Recusado,
    Cancelado,
    Estornado
}

/// <summary>Desfecho de uma confirmação vinda do gateway.</summary>
public enum ResultadoConfirmacao
{
    /// <summary>Estava pendente: agora está aprovado.</summary>
    Confirmado,

    /// <summary>Já estava aprovado (notificação repetida).</summary>
    JaConfirmado,

    /// <summary>
    /// Aprovado depois de recusado ou cancelado (o cliente pagou tarde, ou tentou outro cartão).
    /// Precisa de estorno automático (regras 1 e 3 da seção 5 dos contratos).
    /// </summary>
    Tardio,

    /// <summary>Já foi estornado: nada a fazer.</summary>
    JaEstornado
}

/// <summary>Cobrança de um orçamento aprovado, no Mercado Pago (ou no gateway Fake).</summary>
public sealed class Pagamento
{
    private Pagamento()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public decimal Valor { get; private set; }
    public StatusPagamento Status { get; private set; }
    public string? PreferenceId { get; private set; }
    public string? LinkPagamento { get; private set; }
    public string? MpPaymentId { get; private set; }
    public string? RefundId { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? ConfirmadoEm { get; private set; }
    public DateTime? RecusadoEm { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public DateTime? EstornadoEm { get; private set; }

    /// <summary>Concorrência otimista (coluna de sistema <c>xmin</c> do PostgreSQL).</summary>
    public uint Versao { get; private set; }

    public static Pagamento Criar(Orcamento orcamento, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(orcamento);
        if (orcamento.Status != StatusOrcamento.Aprovado)
            throw new RegraDeNegocioException($"O orçamento da OS {orcamento.NumeroOS} está {orcamento.Status}; só um orçamento aprovado gera pagamento.");

        return new Pagamento
        {
            Id = Guid.NewGuid(),
            OrcamentoId = orcamento.Id,
            Valor = orcamento.ValorTotal,
            Status = StatusPagamento.Pendente,
            CriadoEm = agora
        };
    }

    /// <summary>Guarda a preferência criada no gateway e o link que vai para o cliente.</summary>
    public void RegistrarCobranca(string preferenceId, string linkPagamento)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferenceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(linkPagamento);
        PreferenceId = preferenceId;
        LinkPagamento = linkPagamento;
    }

    public ResultadoConfirmacao Confirmar(string mpPaymentId, DateTime agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        switch (Status)
        {
            case StatusPagamento.Aprovado:
                return ResultadoConfirmacao.JaConfirmado;
            case StatusPagamento.Estornado:
                return ResultadoConfirmacao.JaEstornado;
        }

        var tardio = Status is StatusPagamento.Recusado or StatusPagamento.Cancelado;
        Status = StatusPagamento.Aprovado;
        MpPaymentId = mpPaymentId;
        ConfirmadoEm = agora;
        return tardio ? ResultadoConfirmacao.Tardio : ResultadoConfirmacao.Confirmado;
    }

    /// <summary>A primeira recusa encerra (regra v1 da seção 5). Fora de <c>Pendente</c>, não faz nada.</summary>
    public bool Recusar(string? mpPaymentId, string? motivo, DateTime agora)
    {
        if (Status != StatusPagamento.Pendente)
            return false;

        Status = StatusPagamento.Recusado;
        MpPaymentId = mpPaymentId ?? MpPaymentId;
        MotivoRecusa = string.IsNullOrWhiteSpace(motivo) ? "recusado pelo gateway" : motivo.Trim();
        RecusadoEm = agora;
        return true;
    }

    /// <summary>O orçamento foi cancelado antes do pagamento. Fora de <c>Pendente</c>, não faz nada.</summary>
    public bool Cancelar(DateTime agora)
    {
        if (Status != StatusPagamento.Pendente)
            return false;

        Status = StatusPagamento.Cancelado;
        CanceladoEm = agora;
        return true;
    }

    /// <summary>
    /// Estorno só de pagamento aprovado. Já estornado: devolve <c>false</c> (pedido repetido
    /// não estorna duas vezes).
    /// </summary>
    public bool Estornar(string refundId, DateTime agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refundId);

        if (Status == StatusPagamento.Estornado)
            return false;
        if (Status != StatusPagamento.Aprovado)
            throw new EstornoImpossivelException($"O pagamento {Id} está {Status}; só um pagamento aprovado pode ser estornado.");

        Status = StatusPagamento.Estornado;
        RefundId = refundId;
        EstornadoEm = agora;
        return true;
    }
}
