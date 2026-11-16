namespace Oficina.Billing.Domain;

/// <summary><c>Gerado → Enviado → Aprovado | Recusado | Cancelado | Expirado</c>. Gravado pelo nome.</summary>
public enum StatusOrcamento
{
    Gerado,
    Enviado,
    Aprovado,
    Recusado,
    Cancelado,
    Expirado
}

/// <summary>Cliente no momento da geração (cópia: o cadastro é do os-service).</summary>
public sealed class ClienteOrcamento
{
    private ClienteOrcamento()
    {
    }

    public ClienteOrcamento(int id, string nome, string? email)
    {
        Id = id;
        Nome = nome;
        Email = email;
    }

    public int Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string? Email { get; private set; }
}

/// <summary>Item com o preço do momento da geração (cópia: o catálogo é do os-service).</summary>
public sealed class ItemOrcamento
{
    private ItemOrcamento()
    {
    }

    public ItemOrcamento(string tipo, int referenciaId, string descricao, decimal quantidade, decimal precoUnitario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentException.ThrowIfNullOrWhiteSpace(descricao);
        if (quantidade <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantidade), quantidade, "Quantidade deve ser maior que zero.");
        if (precoUnitario < 0)
            throw new ArgumentOutOfRangeException(nameof(precoUnitario), precoUnitario, "Preço não pode ser negativo.");

        Tipo = tipo;
        ReferenciaId = referenciaId;
        Descricao = descricao;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    /// <summary>"Servico" ou "Peca".</summary>
    public string Tipo { get; private set; } = string.Empty;
    public int ReferenciaId { get; private set; }
    public string Descricao { get; private set; } = string.Empty;
    public decimal Quantidade { get; private set; }
    public decimal PrecoUnitario { get; private set; }

    public decimal Subtotal => Math.Round(Quantidade * PrecoUnitario, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Orçamento de uma OS. O total é sempre calculado aqui, a partir dos itens: o valor que vem
/// no <c>GerarOrcamento</c> é só informativo.
/// </summary>
public sealed class Orcamento
{
    private readonly List<ItemOrcamento> _itens = [];

    private Orcamento()
    {
    }

    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }
    public int OsId { get; private set; }
    public string NumeroOS { get; private set; } = string.Empty;
    public ClienteOrcamento Cliente { get; private set; } = null!;
    public IReadOnlyList<ItemOrcamento> Itens => _itens;
    public decimal ValorTotal { get; private set; }
    public StatusOrcamento Status { get; private set; }
    public DateTime ValidoAte { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime? EnviadoEm { get; private set; }
    public DateTime? DecididoEm { get; private set; }
    public string? MotivoRecusa { get; private set; }
    public DateTime? CanceladoEm { get; private set; }
    public string? MotivoCancelamento { get; private set; }

    /// <summary>Concorrência otimista (coluna de sistema <c>xmin</c> do PostgreSQL).</summary>
    public uint Versao { get; private set; }

    public static Orcamento Gerar(
        Guid correlationId,
        int osId,
        string numeroOS,
        ClienteOrcamento cliente,
        IEnumerable<ItemOrcamento> itens,
        TimeSpan validade,
        DateTime agora)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(osId);
        ArgumentException.ThrowIfNullOrWhiteSpace(numeroOS);
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(itens);
        if (validade <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validade), validade, "A validade deve ser positiva.");

        var orcamento = new Orcamento
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            OsId = osId,
            NumeroOS = numeroOS,
            Cliente = cliente,
            Status = StatusOrcamento.Gerado,
            ValidoAte = agora + validade,
            CriadoEm = agora
        };
        orcamento._itens.AddRange(itens);
        if (orcamento._itens.Count == 0)
            throw new ArgumentException("O orçamento precisa de pelo menos um item.", nameof(itens));

        orcamento.ValorTotal = orcamento._itens.Sum(i => i.Subtotal);
        return orcamento;
    }

    /// <summary>
    /// <c>CancelarOrcamento</c> chegou antes do <c>GerarOrcamento</c> (seção 5, regra 5 dos contratos):
    /// registra o orçamento já cancelado, para o pedido atrasado não gerar uma cobrança.
    /// </summary>
    public static Orcamento RegistrarCancelamentoAntecipado(Guid correlationId, int osId, string numeroOS, string? motivo, DateTime agora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(numeroOS);
        return new Orcamento
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            OsId = osId,
            NumeroOS = numeroOS,
            Cliente = new ClienteOrcamento(0, string.Empty, null),
            Status = StatusOrcamento.Cancelado,
            ValidoAte = agora,
            CriadoEm = agora,
            CanceladoEm = agora,
            MotivoCancelamento = Texto(motivo)
        };
    }

    /// <summary>A notificação ao cliente foi registrada.</summary>
    public void MarcarEnviado(DateTime agora)
    {
        if (Status != StatusOrcamento.Gerado)
            throw new RegraDeNegocioException($"O orçamento da OS {NumeroOS} está {Status}; só um orçamento gerado pode ser enviado.");

        Status = StatusOrcamento.Enviado;
        EnviadoEm = agora;
    }

    /// <summary>Marca como expirado o orçamento enviado e vencido. Devolve se mudou.</summary>
    public bool ExpirarSeVencido(DateTime agora)
    {
        if (Status != StatusOrcamento.Enviado || agora <= ValidoAte)
            return false;

        Status = StatusOrcamento.Expirado;
        return true;
    }

    /// <summary>Só aprova o orçamento enviado e dentro da validade.</summary>
    public void Aprovar(DateTime agora)
    {
        ExigirAberto(agora);
        Status = StatusOrcamento.Aprovado;
        DecididoEm = agora;
    }

    /// <summary>Só recusa o orçamento enviado e dentro da validade.</summary>
    public void Recusar(string? motivo, DateTime agora)
    {
        ExigirAberto(agora);
        Status = StatusOrcamento.Recusado;
        DecididoEm = agora;
        MotivoRecusa = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
    }

    /// <summary>
    /// Compensação da saga (<c>CancelarOrcamento</c>) ou estorno. Vale também para o orçamento
    /// aprovado (regra 2 da seção 5). Em status final, não faz nada e devolve <c>false</c>.
    /// </summary>
    public bool Cancelar(string? motivo, DateTime agora)
    {
        if (Status is StatusOrcamento.Cancelado or StatusOrcamento.Recusado or StatusOrcamento.Expirado)
            return false;

        Status = StatusOrcamento.Cancelado;
        CanceladoEm = agora;
        MotivoCancelamento = Texto(motivo);
        return true;
    }

    private void ExigirAberto(DateTime agora)
    {
        if (Status != StatusOrcamento.Enviado)
            throw new RegraDeNegocioException($"O orçamento da OS {NumeroOS} está {Status}; só um orçamento enviado pode ser aprovado ou recusado.");
        if (agora > ValidoAte)
            throw new RegraDeNegocioException($"O orçamento da OS {NumeroOS} venceu em {ValidoAte:u}.");
    }

    private static string Texto(string? motivo) => string.IsNullOrWhiteSpace(motivo) ? "sem motivo informado" : motivo.Trim();
}
