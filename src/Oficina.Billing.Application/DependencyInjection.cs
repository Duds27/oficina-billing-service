using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oficina.Billing.Application.UseCases;

namespace Oficina.Billing.Application;

public static class DependencyInjection
{
    /// <summary>Registra os casos de uso da camada de aplicação.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Relógio injetável: os testes controlam a validade do orçamento.
        services.TryAddSingleton(TimeProvider.System);

        // Escopados: usam o repositório (DbContext) e o publicador do escopo da mensagem ou da requisição.
        services.AddScoped<EstornoAutomatico>();
        services.AddScoped<ComandosSagaUseCases>();
        services.AddScoped<PagamentosUseCases>();
        return services;
    }
}
