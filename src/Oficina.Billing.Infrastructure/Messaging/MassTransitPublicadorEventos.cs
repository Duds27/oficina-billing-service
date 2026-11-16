using MassTransit;
using Oficina.Billing.Application.Abstractions;

namespace Oficina.Billing.Infrastructure.Messaging;

/// <summary>
/// Publica pelo <see cref="IPublishEndpoint"/> do escopo. Num consumidor, vai para o outbox do
/// consumidor; numa requisição da API, para o bus outbox. Nos dois casos, a mensagem é gravada
/// no mesmo <c>SaveChanges</c> das mudanças no banco.
/// </summary>
internal sealed class MassTransitPublicadorEventos : IPublicadorEventos
{
    private readonly IPublishEndpoint _publish;

    public MassTransitPublicadorEventos(IPublishEndpoint publish) => _publish = publish;

    public Task PublicarAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class =>
        _publish.Publish(mensagem, cancellationToken);
}
