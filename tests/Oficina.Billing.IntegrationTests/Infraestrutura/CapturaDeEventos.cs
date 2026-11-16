using MassTransit;
using Oficina.Contracts;

namespace Oficina.Billing.IntegrationTests.Infraestrutura;

/// <summary>
/// Recebe os eventos que o billing publica, no papel do os-service. Necessário porque o que
/// sai pelo outbox é reenviado como corpo serializado e não aparece tipado no
/// <c>Published</c> do harness; consumido, aparece tipado no <c>Consumed</c>.
/// </summary>
public sealed class CapturaDeEventos :
    IConsumer<OrcamentoGerado>,
    IConsumer<OrcamentoAprovado>,
    IConsumer<OrcamentoRecusado>,
    IConsumer<PagamentoConfirmado>,
    IConsumer<PagamentoRecusado>,
    IConsumer<PagamentoEstornado>
{
    public Task Consume(ConsumeContext<OrcamentoGerado> context) => Task.CompletedTask;

    public Task Consume(ConsumeContext<OrcamentoAprovado> context) => Task.CompletedTask;

    public Task Consume(ConsumeContext<OrcamentoRecusado> context) => Task.CompletedTask;

    public Task Consume(ConsumeContext<PagamentoConfirmado> context) => Task.CompletedTask;

    public Task Consume(ConsumeContext<PagamentoRecusado> context) => Task.CompletedTask;

    public Task Consume(ConsumeContext<PagamentoEstornado> context) => Task.CompletedTask;
}
