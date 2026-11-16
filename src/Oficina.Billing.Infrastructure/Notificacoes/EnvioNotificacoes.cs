using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Billing.Infrastructure.Persistence;

namespace Oficina.Billing.Infrastructure.Notificacoes;

/// <summary>Seção <c>Notificacoes</c>.</summary>
public sealed class NotificacoesOptions
{
    public const string Secao = "Notificacoes";

    public int IntervaloMs { get; set; } = 5000;

    public int Lote { get; set; } = 20;
}

/// <summary>
/// "Envia" os e-mails gravados em <c>NotificacoesOutbox</c>: o envio é simulado com um log,
/// como na Fase 2. O e-mail do cliente sai mascarado no log.
/// </summary>
internal sealed partial class EnvioNotificacoes : BackgroundService
{
    private readonly IServiceScopeFactory _escopos;
    private readonly TimeProvider _relogio;
    private readonly NotificacoesOptions _opcoes;
    private readonly ILogger<EnvioNotificacoes> _logger;

    public EnvioNotificacoes(IServiceScopeFactory escopos, TimeProvider relogio, IOptions<NotificacoesOptions> opcoes, ILogger<EnvioNotificacoes> logger)
    {
        _escopos = escopos;
        _relogio = relogio;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opcoes.IntervaloMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await EnviarPendentesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFalha(ex);
            }
        }
    }

    internal async Task<int> EnviarPendentesAsync(CancellationToken cancellationToken)
    {
        await using var escopo = _escopos.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<BillingDbContext>();

        var pendentes = await db.Notificacoes
            .Where(n => n.EnviadaEm == null)
            .OrderBy(n => n.CriadaEm)
            .Take(_opcoes.Lote)
            .ToListAsync(cancellationToken);

        var agora = _relogio.GetUtcNow().UtcDateTime;
        foreach (var notificacao in pendentes)
        {
            LogEnviada(Mascarar(notificacao.Destinatario), notificacao.Assunto, notificacao.OrcamentoId);
            notificacao.MarcarEnviada(agora);
        }

        await db.SaveChangesAsync(cancellationToken);
        return pendentes.Count;
    }

    /// <summary><c>maria@example.com</c> → <c>m***@example.com</c>.</summary>
    internal static string Mascarar(string email)
    {
        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        return arroba <= 0 ? "***" : $"{email[0]}***{email[arroba..]}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail enviado (simulado) para {Destinatario}: {Assunto} (orçamento {OrcamentoId})")]
    private partial void LogEnviada(string destinatario, string assunto, Guid orcamentoId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao enviar as notificações pendentes; nova tentativa no próximo ciclo")]
    private partial void LogFalha(Exception ex);
}
