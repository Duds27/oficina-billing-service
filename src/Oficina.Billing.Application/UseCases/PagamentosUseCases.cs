using Microsoft.Extensions.Logging;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.Mensagens;
using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.Application.UseCases;

/// <summary>
/// Notificação de pagamento (webhook do Mercado Pago ou simulação no gateway Fake). O status
/// vem sempre da consulta ao gateway, nunca do corpo da notificação.
/// </summary>
public sealed partial class PagamentosUseCases
{
    private readonly IBillingRepositorio _repositorio;
    private readonly IGatewayPagamento _gateway;
    private readonly IPublicadorEventos _publicador;
    private readonly EstornoAutomatico _estorno;
    private readonly TimeProvider _relogio;
    private readonly ILogger<PagamentosUseCases> _logger;

    public PagamentosUseCases(
        IBillingRepositorio repositorio,
        IGatewayPagamento gateway,
        IPublicadorEventos publicador,
        EstornoAutomatico estorno,
        TimeProvider relogio,
        ILogger<PagamentosUseCases> logger)
    {
        _repositorio = repositorio;
        _gateway = gateway;
        _publicador = publicador;
        _estorno = estorno;
        _relogio = relogio;
        _logger = logger;
    }

    public async Task<ResultadoProcessamento> ProcessarNotificacaoAsync(ProcessarNotificacaoPagamento notificacao, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notificacao);

        var situacao = await _gateway.ConsultarPagamentoAsync(notificacao.MpPaymentId, cancellationToken);
        if (situacao?.PagamentoId is not { } pagamentoId)
        {
            LogPagamentoDesconhecido(notificacao.MpPaymentId);
            return ResultadoProcessamento.Ignorada;
        }

        var pagamento = await _repositorio.ObterPagamentoAsync(pagamentoId, cancellationToken);
        var orcamento = pagamento is null ? null : await _repositorio.ObterOrcamentoAsync(pagamento.OrcamentoId, cancellationToken);
        if (pagamento is null || orcamento is null)
        {
            LogPagamentoDesconhecido(notificacao.MpPaymentId);
            return ResultadoProcessamento.Ignorada;
        }

        var agora = _relogio.GetUtcNow().UtcDateTime;
        var processada = situacao.Status switch
        {
            StatusNoGateway.Aprovado => await AprovadoAsync(orcamento, pagamento, situacao, agora, cancellationToken),
            StatusNoGateway.Recusado => await RecusadoAsync(orcamento, pagamento, situacao, agora, cancellationToken),
            _ => false // Pendente ou já estornado: espera a próxima notificação.
        };

        if (!processada)
            return ResultadoProcessamento.Ignorada;

        await _repositorio.SalvarAsync(cancellationToken);
        return ResultadoProcessamento.Processada;
    }

    private async Task<bool> AprovadoAsync(Orcamento orcamento, Pagamento pagamento, SituacaoPagamento situacao, DateTime agora, CancellationToken cancellationToken)
    {
        if (situacao.Valor != pagamento.Valor)
            LogValorDivergente(orcamento.NumeroOS, pagamento.Valor, situacao.Valor);

        switch (pagamento.Confirmar(situacao.MpPaymentId, agora))
        {
            case ResultadoConfirmacao.Confirmado when orcamento.Status == StatusOrcamento.Cancelado:
                // Regra 1: pagou depois do cancelamento.
                await _estorno.EstornarAsync(orcamento, pagamento, "pagamento aprovado depois do cancelamento", agora, cancellationToken);
                return true;
            case ResultadoConfirmacao.Confirmado:
                await _publicador.PublicarAsync(FabricaMensagens.PagamentoConfirmado(orcamento, pagamento, agora), cancellationToken);
                return true;
            case ResultadoConfirmacao.Tardio:
                // Regras 1 e 3: aprovado depois de recusado ou cancelado.
                await _estorno.EstornarAsync(orcamento, pagamento, "pagamento aprovado depois de encerrado", agora, cancellationToken);
                return true;
            default:
                return false; // Notificação repetida.
        }
    }

    private async Task<bool> RecusadoAsync(Orcamento orcamento, Pagamento pagamento, SituacaoPagamento situacao, DateTime agora, CancellationToken cancellationToken)
    {
        if (!pagamento.Recusar(situacao.MpPaymentId, situacao.Detalhe, agora))
            return false;

        await _publicador.PublicarAsync(FabricaMensagens.PagamentoRecusado(orcamento, pagamento, agora), cancellationToken);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notificação do pagamento {MpPaymentId} no gateway sem pagamento nosso correspondente; ignorada")]
    private partial void LogPagamentoDesconhecido(string mpPaymentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OS {NumeroOS}: valor cobrado {Esperado} difere do pago {Pago}")]
    private partial void LogValorDivergente(string numeroOS, decimal esperado, decimal pago);
}
