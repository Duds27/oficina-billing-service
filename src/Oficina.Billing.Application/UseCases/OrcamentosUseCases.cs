using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.DTOs;
using Oficina.Billing.Application.Mensagens;
using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.Application.UseCases;

/// <summary>Consulta de orçamentos e pagamentos, e a decisão do cliente (API).</summary>
public sealed partial class OrcamentosUseCases
{
    private readonly IBillingRepositorio _repositorio;
    private readonly IGatewayPagamento _gateway;
    private readonly IPublicadorEventos _publicador;
    private readonly TimeProvider _relogio;
    private readonly PagamentosOptions _opcoes;
    private readonly ILogger<OrcamentosUseCases> _logger;

    public OrcamentosUseCases(
        IBillingRepositorio repositorio,
        IGatewayPagamento gateway,
        IPublicadorEventos publicador,
        TimeProvider relogio,
        IOptions<PagamentosOptions> opcoes,
        ILogger<OrcamentosUseCases> logger)
    {
        _repositorio = repositorio;
        _gateway = gateway;
        _publicador = publicador;
        _relogio = relogio;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<Resultado<OrcamentoDto>> ObterAsync(Guid id, Solicitante quem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quem);
        var orcamento = await _repositorio.ObterOrcamentoAsync(id, cancellationToken);
        if (orcamento is null)
            return NaoEncontrado<OrcamentoDto>(id);
        if (!quem.PodeVer(orcamento.Cliente.Id))
            return Proibido<OrcamentoDto>();

        var pagamento = await _repositorio.ObterPagamentoDoOrcamentoAsync(orcamento.Id, cancellationToken);
        return Resultado<OrcamentoDto>.Ok(orcamento.ParaDto(pagamento));
    }

    /// <summary>Orçamentos da OS que o solicitante pode ver (o cliente só vê os dele).</summary>
    public async Task<IReadOnlyList<OrcamentoDto>> ListarDaOsAsync(int osId, Solicitante quem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quem);
        var dtos = new List<OrcamentoDto>();
        foreach (var orcamento in await _repositorio.ListarOrcamentosDaOsAsync(osId, cancellationToken))
        {
            if (!quem.PodeVer(orcamento.Cliente.Id))
                continue;
            dtos.Add(orcamento.ParaDto(await _repositorio.ObterPagamentoDoOrcamentoAsync(orcamento.Id, cancellationToken)));
        }

        return dtos;
    }

    public async Task<Resultado<PagamentoDto>> ObterPagamentoAsync(Guid id, Solicitante quem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quem);
        var pagamento = await _repositorio.ObterPagamentoAsync(id, cancellationToken);
        var orcamento = pagamento is null ? null : await _repositorio.ObterOrcamentoAsync(pagamento.OrcamentoId, cancellationToken);
        if (pagamento is null || orcamento is null)
            return Resultado<PagamentoDto>.Falha(StatusResultado.NaoEncontrado, $"Pagamento {id} não encontrado.");
        if (!quem.PodeVer(orcamento.Cliente.Id))
            return Proibido<PagamentoDto>();

        return Resultado<PagamentoDto>.Ok(pagamento.ParaDto());
    }

    /// <summary>
    /// Aprovação ou recusa. Aprovando: cria o pagamento, a cobrança no gateway
    /// (<c>X-Idempotency-Key</c> = id do pagamento) e publica <c>OrcamentoAprovado</c> com o link,
    /// tudo numa gravação só. Se o gateway falhar, nada é gravado e o cliente pode tentar de novo.
    /// </summary>
    public async Task<Resultado<OrcamentoDto>> DecidirAsync(Guid id, bool aprovado, string? motivo, Solicitante quem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quem);
        var orcamento = await _repositorio.ObterOrcamentoAsync(id, cancellationToken);
        if (orcamento is null)
            return NaoEncontrado<OrcamentoDto>(id);
        if (!quem.PodeDecidir(orcamento.Cliente.Id))
            return Proibido<OrcamentoDto>();

        var agora = _relogio.GetUtcNow().UtcDateTime;
        try
        {
            if (orcamento.ExpirarSeVencido(agora))
            {
                await _repositorio.SalvarAsync(cancellationToken);
                MetricasBilling.Decisao("expirado");
                return Resultado<OrcamentoDto>.Falha(StatusResultado.Conflito, $"O orçamento da OS {orcamento.NumeroOS} venceu em {orcamento.ValidoAte:u}.");
            }

            Pagamento? pagamento = null;
            if (aprovado)
            {
                orcamento.Aprovar(agora);
                pagamento = Pagamento.Criar(orcamento, agora);
                var cobranca = await _gateway.CriarCobrancaAsync(
                    new NovaCobranca(pagamento.Id, orcamento.NumeroOS, pagamento.Valor, orcamento.Cliente.Email, agora + _opcoes.PrazoPagamento),
                    cancellationToken);
                pagamento.RegistrarCobranca(cobranca.PreferenceId, cobranca.LinkPagamento);
                _repositorio.Adicionar(pagamento);
                await _publicador.PublicarAsync(FabricaMensagens.OrcamentoAprovado(orcamento, pagamento, agora), cancellationToken);
            }
            else
            {
                orcamento.Recusar(motivo, agora);
                await _publicador.PublicarAsync(FabricaMensagens.OrcamentoRecusado(orcamento, agora), cancellationToken);
            }

            await _repositorio.SalvarAsync(cancellationToken);
            MetricasBilling.Decisao(aprovado ? "aprovado" : "recusado");
            LogDecidido(orcamento.NumeroOS, orcamento.Status);
            return Resultado<OrcamentoDto>.Ok(orcamento.ParaDto(pagamento));
        }
        catch (RegraDeNegocioException ex)
        {
            return Resultado<OrcamentoDto>.Falha(StatusResultado.Conflito, ex.Message);
        }
        catch (ConflitoDeConcorrenciaException)
        {
            return Resultado<OrcamentoDto>.Falha(StatusResultado.Conflito, $"O orçamento da OS {orcamento.NumeroOS} mudou durante a operação. Consulte de novo.");
        }
        catch (Exception ex) when (ex is GatewayIndisponivelException or GatewayRecusouException)
        {
            LogGatewayIndisponivel(ex, orcamento.NumeroOS);
            return Resultado<OrcamentoDto>.Falha(StatusResultado.Indisponivel, "O meio de pagamento não respondeu. Tente aprovar de novo em instantes.");
        }
    }

    /// <summary>
    /// Registra uma notificação de pagamento (webhook ou simulação) para processamento assíncrono:
    /// grava <c>ProcessarNotificacaoPagamento</c> no outbox e volta.
    /// </summary>
    public async Task RegistrarNotificacaoAsync(string mpPaymentId, string? requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);
        await _publicador.PublicarAsync(new ProcessarNotificacaoPagamento
        {
            MpPaymentId = mpPaymentId,
            RequestId = requestId,
            RecebidaEm = _relogio.GetUtcNow().UtcDateTime
        }, cancellationToken);
        await _repositorio.SalvarAsync(cancellationToken);
    }

    private static Resultado<T> NaoEncontrado<T>(Guid id) => Resultado<T>.Falha(StatusResultado.NaoEncontrado, $"Orçamento {id} não encontrado.");

    private static Resultado<T> Proibido<T>() => Resultado<T>.Falha(StatusResultado.Proibido, "O orçamento é de outro cliente.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Orçamento da OS {NumeroOS} {Status} pelo cliente")]
    private partial void LogDecidido(string numeroOS, StatusOrcamento status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OS {NumeroOS}: falha no gateway na aprovação; nada foi gravado")]
    private partial void LogGatewayIndisponivel(Exception ex, string numeroOS);
}
