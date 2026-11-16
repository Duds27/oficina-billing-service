namespace Oficina.Billing.Domain;

/// <summary>Operação que a regra de negócio não permite no estado atual (a API responde 409).</summary>
public class RegraDeNegocioException : Exception
{
    public RegraDeNegocioException(string message) : base(message)
    {
    }

    public RegraDeNegocioException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public RegraDeNegocioException()
    {
    }
}

/// <summary>
/// Pedido de estorno de um pagamento que não foi aprovado. Não se resolve tentando de novo:
/// a mensagem vai direto para a fila de erro, com alerta crítico.
/// </summary>
public sealed class EstornoImpossivelException : RegraDeNegocioException
{
    public EstornoImpossivelException(string message) : base(message)
    {
    }

    public EstornoImpossivelException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public EstornoImpossivelException()
    {
    }
}
