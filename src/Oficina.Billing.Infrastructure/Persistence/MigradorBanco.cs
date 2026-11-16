using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oficina.Billing.Infrastructure.Persistence;

/// <summary>Seção <c>Database</c>.</summary>
public sealed class DatabaseOptions
{
    public const string Secao = "Database";

    /// <summary>Aplica as migrations ao subir (mesmo padrão do os-service).</summary>
    public bool AplicarMigrations { get; set; } = true;
}

/// <summary>
/// Aplica as migrations na inicialização, com novas tentativas: no docker-compose e no EKS o
/// banco pode demorar a aceitar conexões. Registrado antes do MassTransit, para os consumidores
/// só começarem com o schema pronto.
/// </summary>
internal sealed partial class MigradorBanco : IHostedService
{
    private const int Tentativas = 15;

    private readonly IServiceScopeFactory _escopos;
    private readonly IOptions<DatabaseOptions> _opcoes;
    private readonly ILogger<MigradorBanco> _logger;

    public MigradorBanco(IServiceScopeFactory escopos, IOptions<DatabaseOptions> opcoes, ILogger<MigradorBanco> logger)
    {
        _escopos = escopos;
        _opcoes = opcoes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_opcoes.Value.AplicarMigrations)
            return;

        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await using var escopo = _escopos.CreateAsyncScope();
                var db = escopo.ServiceProvider.GetRequiredService<BillingDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                LogMigrado();
                return;
            }
            catch (Exception ex) when (tentativa < Tentativas && ex is not OperationCanceledException)
            {
                LogTentativaFalhou(tentativa, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations do banco do billing aplicadas")]
    private partial void LogMigrado();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tentativa {Tentativa} de aplicar as migrations falhou: {Erro}")]
    private partial void LogTentativaFalhou(int tentativa, string erro);
}
