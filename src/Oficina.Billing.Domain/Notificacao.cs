using System.Globalization;

namespace Oficina.Billing.Domain;

/// <summary>
/// E-mail ao cliente, gravado na mesma transação do orçamento (tabela <c>NotificacoesOutbox</c>)
/// e enviado depois por um serviço em segundo plano. O envio é simulado (log), como na Fase 2.
/// </summary>
public sealed class Notificacao
{
    /// <summary>Valores e datas no formato brasileiro, qualquer que seja a cultura do servidor.</summary>
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private Notificacao()
    {
    }

    public Guid Id { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public string Destinatario { get; private set; } = string.Empty;
    public string Assunto { get; private set; } = string.Empty;
    public string Corpo { get; private set; } = string.Empty;
    public DateTime CriadaEm { get; private set; }
    public DateTime? EnviadaEm { get; private set; }

    /// <summary>Resumo do orçamento com o link de aprovação. Sem e-mail no cadastro, não há notificação.</summary>
    public static Notificacao? DoOrcamento(Orcamento orcamento, string linkAprovacao, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(orcamento);
        if (string.IsNullOrWhiteSpace(orcamento.Cliente.Email))
            return null;

        var linhas = orcamento.Itens.Select(i =>
            string.Create(PtBr, $"- {i.Descricao}: {i.Quantidade:0.##} x R$ {i.PrecoUnitario:N2} = R$ {i.Subtotal:N2}"));

        return new Notificacao
        {
            Id = Guid.NewGuid(),
            OrcamentoId = orcamento.Id,
            Destinatario = orcamento.Cliente.Email,
            Assunto = string.Create(PtBr, $"Orçamento da OS {orcamento.NumeroOS}: R$ {orcamento.ValorTotal:N2}"),
            Corpo = string.Join('\n',
            [
                $"Olá, {orcamento.Cliente.Nome}.",
                $"O orçamento da OS {orcamento.NumeroOS} está pronto:",
                .. linhas,
                string.Create(PtBr, $"Total: R$ {orcamento.ValorTotal:N2}. Válido até {orcamento.ValidoAte:dd/MM/yyyy HH:mm} (UTC)."),
                $"Para aprovar ou recusar: {linkAprovacao}"
            ]),
            CriadaEm = agora
        };
    }

    public void MarcarEnviada(DateTime agora) => EnviadaEm ??= agora;
}
