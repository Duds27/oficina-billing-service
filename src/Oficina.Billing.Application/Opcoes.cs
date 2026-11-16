namespace Oficina.Billing.Application;

/// <summary>Seção <c>Orcamento</c>.</summary>
public sealed class OrcamentoOptions
{
    public const string Secao = "Orcamento";

    /// <summary>Validade do orçamento. Igual ao prazo de aprovação da saga (48 h; 2 min no compose).</summary>
    public TimeSpan Validade { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Link de aprovação no e-mail. <c>{0}</c> é o id do orçamento.</summary>
    public string UrlAprovacao { get; set; } = "http://localhost:8082/api/orcamentos/{0}";
}

/// <summary>Seção <c>Pagamentos</c>.</summary>
public sealed class PagamentosOptions
{
    public const string Secao = "Pagamentos";

    /// <summary><c>Fake</c> (testes, CI, compose) ou <c>MercadoPago</c> (sandbox).</summary>
    public string Gateway { get; set; } = "Fake";

    /// <summary>Até quando o link de pagamento vale. Igual ao prazo de pagamento da saga.</summary>
    public TimeSpan PrazoPagamento { get; set; } = TimeSpan.FromHours(48);
}
