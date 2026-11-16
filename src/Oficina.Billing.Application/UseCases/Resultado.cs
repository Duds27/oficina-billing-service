namespace Oficina.Billing.Application.UseCases;

public enum StatusResultado
{
    Ok,
    Invalido,
    NaoEncontrado,
    Proibido,
    Conflito,
    Indisponivel
}

/// <summary>Resultado de uma operação da API: o valor, ou o motivo de não ter dado certo.</summary>
public sealed record Resultado<T>(StatusResultado Status, T? Valor, string? Erro)
{
    public static Resultado<T> Ok(T valor) => new(StatusResultado.Ok, valor, null);

    public static Resultado<T> Falha(StatusResultado status, string erro) => new(status, default, erro);
}

/// <summary>
/// Quem chamou a API, a partir do token. A equipe da oficina (<c>Admin</c>, <c>Mecanico</c>) vê
/// tudo; o atendente (<c>Admin</c>) também registra a decisão do cliente; o cliente
/// (<c>Cliente</c>, login por CPF na Lambda) só vê e decide os próprios orçamentos.
/// </summary>
public sealed record Solicitante(bool DaOficina, bool Atendente, int? ClienteId)
{
    public bool PodeVer(int clienteDoOrcamento) => DaOficina || ClienteId == clienteDoOrcamento;

    public bool PodeDecidir(int clienteDoOrcamento) => Atendente || ClienteId == clienteDoOrcamento;
}
