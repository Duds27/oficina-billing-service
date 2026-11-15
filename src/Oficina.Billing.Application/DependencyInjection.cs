using Microsoft.Extensions.DependencyInjection;

namespace Oficina.Billing.Application;

public static class DependencyInjection
{
    /// <summary>Registra os casos de uso e validadores da camada de aplicação.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
