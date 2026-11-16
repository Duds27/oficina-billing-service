using MassTransit;
using Microsoft.Extensions.Logging;
using Oficina.Billing.Application.UseCases;
using Oficina.Contracts;

namespace Oficina.Billing.Infrastructure.Messaging;

public sealed class GerarOrcamentoConsumer : IConsumer<GerarOrcamento>
{
    private readonly ComandosSagaUseCases _useCases;
    private readonly ILogger<GerarOrcamentoConsumer> _logger;

    public GerarOrcamentoConsumer(ComandosSagaUseCases useCases, ILogger<GerarOrcamentoConsumer> logger)
    {
        _useCases = useCases;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<GerarOrcamento> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resultado = await _useCases.GerarOrcamentoAsync(context.Message, context.CancellationToken);
        _logger.MensagemProcessada(nameof(GerarOrcamento), context.Message.NumeroOS, resultado);
    }
}

public sealed class CancelarOrcamentoConsumer : IConsumer<CancelarOrcamento>
{
    private readonly ComandosSagaUseCases _useCases;
    private readonly ILogger<CancelarOrcamentoConsumer> _logger;

    public CancelarOrcamentoConsumer(ComandosSagaUseCases useCases, ILogger<CancelarOrcamentoConsumer> logger)
    {
        _useCases = useCases;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CancelarOrcamento> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resultado = await _useCases.CancelarOrcamentoAsync(context.Message, context.CancellationToken);
        _logger.MensagemProcessada(nameof(CancelarOrcamento), context.Message.NumeroOS, resultado);
    }
}

public sealed class EstornarPagamentoConsumer : IConsumer<EstornarPagamento>
{
    private readonly ComandosSagaUseCases _useCases;
    private readonly ILogger<EstornarPagamentoConsumer> _logger;

    public EstornarPagamentoConsumer(ComandosSagaUseCases useCases, ILogger<EstornarPagamentoConsumer> logger)
    {
        _useCases = useCases;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EstornarPagamento> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resultado = await _useCases.EstornarPagamentoAsync(context.Message, context.CancellationToken);
        _logger.MensagemProcessada(nameof(EstornarPagamento), context.Message.NumeroOS, resultado);
    }
}

public sealed class ProcessarNotificacaoPagamentoConsumer : IConsumer<ProcessarNotificacaoPagamento>
{
    private readonly PagamentosUseCases _useCases;
    private readonly ILogger<ProcessarNotificacaoPagamentoConsumer> _logger;

    public ProcessarNotificacaoPagamentoConsumer(PagamentosUseCases useCases, ILogger<ProcessarNotificacaoPagamentoConsumer> logger)
    {
        _useCases = useCases;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProcessarNotificacaoPagamento> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var resultado = await _useCases.ProcessarNotificacaoAsync(context.Message, context.CancellationToken);
        _logger.NotificacaoProcessada(context.Message.MpPaymentId, context.Message.RequestId, resultado);
    }
}

/// <summary>
/// Estorno que falhou de vez (retries esgotados, ou pagamento que não pode ser estornado).
/// A mensagem fica em <c>billing-estornar-pagamento_error</c>, a saga espera em
/// <c>AguardandoEstorno</c> e alguém precisa agir: o log crítico vira alerta no New Relic.
/// </summary>
public sealed class FalhaDeEstornoConsumer : IConsumer<Fault<EstornarPagamento>>
{
    private readonly ILogger<FalhaDeEstornoConsumer> _logger;

    public FalhaDeEstornoConsumer(ILogger<FalhaDeEstornoConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<Fault<EstornarPagamento>> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var comando = context.Message.Message;
        var erro = context.Message.Exceptions.FirstOrDefault();
        _logger.EstornoFalhouDeVez(comando.NumeroOS, comando.PagamentoId, comando.CorrelationId, erro?.ExceptionType ?? "?", erro?.Message ?? "?");
        return Task.CompletedTask;
    }
}

internal static partial class LogsMensageria
{
    [LoggerMessage(Level = LogLevel.Information, Message = "{TipoMensagem} da OS {NumeroOS}: {Resultado}")]
    public static partial void MensagemProcessada(this ILogger logger, string tipoMensagem, string numeroOS, ResultadoProcessamento resultado);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notificação do pagamento {MpPaymentId} (request {RequestId}): {Resultado}")]
    public static partial void NotificacaoProcessada(this ILogger logger, string mpPaymentId, string? requestId, ResultadoProcessamento resultado);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "ALERTA: estorno do pagamento {PagamentoId} da OS {NumeroOS} falhou de vez ({TipoErro}: {Erro}). " +
                  "A saga {CorrelationId} está esperando em AguardandoEstorno. Corrija a causa e mova a mensagem de " +
                  "billing-estornar-pagamento_error para billing-estornar-pagamento.")]
    public static partial void EstornoFalhouDeVez(this ILogger logger, string numeroOS, Guid pagamentoId, Guid correlationId, string tipoErro, string erro);
}
