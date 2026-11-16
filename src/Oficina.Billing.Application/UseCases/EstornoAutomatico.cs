using Microsoft.Extensions.Logging;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.Mensagens;
using Oficina.Billing.Domain;

namespace Oficina.Billing.Application.UseCases;

/// <summary>
/// Estorno no gateway + baixa no pagamento + cancelamento do orçamento + evento
/// <c>PagamentoEstornado</c>. Usado pela compensação da saga e pelos estornos automáticos
/// (pagamento aprovado depois do cancelamento ou da recusa). Não salva: quem chama salva.
/// </summary>
public sealed partial class EstornoAutomatico
{
    private readonly IGatewayPagamento _gateway;
    private readonly IPublicadorEventos _publicador;
    private readonly ILogger<EstornoAutomatico> _logger;

    public EstornoAutomatico(IGatewayPagamento gateway, IPublicadorEventos publicador, ILogger<EstornoAutomatico> logger)
    {
        _gateway = gateway;
        _publicador = publicador;
        _logger = logger;
    }

    /// <exception cref="EstornoImpossivelException">O pagamento não está aprovado.</exception>
    public async Task EstornarAsync(Orcamento orcamento, Pagamento pagamento, string motivo, DateTime agora, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orcamento);
        ArgumentNullException.ThrowIfNull(pagamento);

        if (pagamento.Status != StatusPagamento.Aprovado || string.IsNullOrEmpty(pagamento.MpPaymentId))
            throw new EstornoImpossivelException(
                $"O pagamento {pagamento.Id} da OS {orcamento.NumeroOS} está {pagamento.Status}; só um pagamento aprovado pode ser estornado.");

        // A chave é o id do pagamento: numa nova tentativa, o gateway devolve o mesmo estorno.
        var estorno = await _gateway.EstornarAsync(pagamento.MpPaymentId, pagamento.Id, cancellationToken);
        pagamento.Estornar(estorno.RefundId, agora);
        orcamento.Cancelar($"Pagamento estornado: {motivo}", agora);
        LogEstornado(orcamento.NumeroOS, pagamento.Id, pagamento.Valor, motivo);

        await _publicador.PublicarAsync(FabricaMensagens.PagamentoEstornado(orcamento, pagamento, agora), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OS {NumeroOS}: pagamento {PagamentoId} de {Valor} estornado ({Motivo})")]
    private partial void LogEstornado(string numeroOS, Guid pagamentoId, decimal valor, string motivo);
}
