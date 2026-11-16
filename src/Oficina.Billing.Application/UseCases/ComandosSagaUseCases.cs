using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.Mensagens;
using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.Application.UseCases;

/// <summary>Desfecho do processamento de uma mensagem, para log e testes.</summary>
public enum ResultadoProcessamento
{
    /// <summary>Mudou o estado e/ou publicou evento.</summary>
    Processada,

    /// <summary>Válida, mas sem efeito (repetida ou atrasada).</summary>
    Ignorada
}

/// <summary>
/// Comandos da saga do os-service. A mensagem repetida (mesmo MessageId) já é descartada pelo
/// inbox do MassTransit; aqui ficam as regras para o pedido repetido com outro MessageId ou fora de ordem.
/// </summary>
public sealed partial class ComandosSagaUseCases
{
    private readonly IBillingRepositorio _repositorio;
    private readonly IPublicadorEventos _publicador;
    private readonly EstornoAutomatico _estorno;
    private readonly TimeProvider _relogio;
    private readonly OrcamentoOptions _opcoes;
    private readonly ILogger<ComandosSagaUseCases> _logger;

    public ComandosSagaUseCases(
        IBillingRepositorio repositorio,
        IPublicadorEventos publicador,
        EstornoAutomatico estorno,
        TimeProvider relogio,
        IOptions<OrcamentoOptions> opcoes,
        ILogger<ComandosSagaUseCases> logger)
    {
        _repositorio = repositorio;
        _publicador = publicador;
        _estorno = estorno;
        _relogio = relogio;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    /// <summary>Gera o orçamento, registra o e-mail ao cliente e responde <c>OrcamentoGerado</c>.</summary>
    public async Task<ResultadoProcessamento> GerarOrcamentoAsync(GerarOrcamento comando, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var existente = await _repositorio.ObterOrcamentoDaSagaAsync(comando.CorrelationId, cancellationToken);
        if (existente is not null)
        {
            // Pedido repetido, ou o cancelamento chegou antes (regra 5).
            LogOrcamentoJaExiste(comando.NumeroOS, existente.Status);
            return ResultadoProcessamento.Ignorada;
        }

        var agora = Agora();
        var itens = comando.Itens.Select(i => new ItemOrcamento(i.Tipo, i.ReferenciaId, i.Descricao, i.Quantidade, i.PrecoUnitario));
        var orcamento = Orcamento.Gerar(
            comando.CorrelationId,
            comando.OsId,
            comando.NumeroOS,
            new ClienteOrcamento(comando.Cliente.Id, comando.Cliente.Nome, comando.Cliente.Email),
            itens,
            _opcoes.Validade,
            agora);

        if (comando.ValorTotal != orcamento.ValorTotal)
            LogTotalDivergente(comando.NumeroOS, comando.ValorTotal, orcamento.ValorTotal);

        _repositorio.Adicionar(orcamento);
        var link = string.Format(System.Globalization.CultureInfo.InvariantCulture, _opcoes.UrlAprovacao, orcamento.Id);
        if (Notificacao.DoOrcamento(orcamento, link, agora) is { } email)
            _repositorio.Adicionar(email);
        else
            LogClienteSemEmail(comando.NumeroOS);
        orcamento.MarcarEnviado(agora);

        await _publicador.PublicarAsync(FabricaMensagens.OrcamentoGerado(orcamento, agora), cancellationToken);
        await _repositorio.SalvarAsync(cancellationToken);
        return ResultadoProcessamento.Processada;
    }

    /// <summary>
    /// Compensação (C2): cancela o orçamento e o pagamento pendente. Se o pagamento já tinha sido
    /// aprovado (pagou no limite do prazo), estorna sozinho (regras 1 e 2 da seção 5).
    /// </summary>
    public async Task<ResultadoProcessamento> CancelarOrcamentoAsync(CancelarOrcamento comando, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var agora = Agora();
        var orcamento = await _repositorio.ObterOrcamentoDaSagaAsync(comando.CorrelationId, cancellationToken);
        if (orcamento is null)
        {
            // Regra 5 da seção 5: registra o cancelamento e conclui sem erro.
            _repositorio.Adicionar(Orcamento.RegistrarCancelamentoAntecipado(
                comando.CorrelationId, comando.OsId, comando.NumeroOS, comando.Motivo, agora));
            await _repositorio.SalvarAsync(cancellationToken);
            return ResultadoProcessamento.Processada;
        }

        var mudou = orcamento.Cancelar(comando.Motivo, agora);
        var pagamento = await _repositorio.ObterPagamentoDoOrcamentoAsync(orcamento.Id, cancellationToken);
        if (pagamento is not null)
        {
            mudou |= pagamento.Cancelar(agora);
            if (pagamento.Status == StatusPagamento.Aprovado)
            {
                await _estorno.EstornarAsync(orcamento, pagamento, "orçamento cancelado depois do pagamento", agora, cancellationToken);
                mudou = true;
            }
        }

        if (!mudou)
            return ResultadoProcessamento.Ignorada;

        await _repositorio.SalvarAsync(cancellationToken);
        return ResultadoProcessamento.Processada;
    }

    /// <summary>
    /// Compensação (C3): estorna no gateway e responde <c>PagamentoEstornado</c>. Pedido repetido
    /// não estorna de novo, mas responde de novo (a saga espera a resposta em <c>AguardandoEstorno</c>).
    /// </summary>
    /// <exception cref="EstornoImpossivelException">O pagamento não existe ou não foi aprovado.</exception>
    public async Task<ResultadoProcessamento> EstornarPagamentoAsync(EstornarPagamento comando, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var pagamento = await _repositorio.ObterPagamentoAsync(comando.PagamentoId, cancellationToken)
            ?? throw new EstornoImpossivelException($"Pagamento {comando.PagamentoId} da OS {comando.NumeroOS} não existe.");
        var orcamento = await _repositorio.ObterOrcamentoAsync(pagamento.OrcamentoId, cancellationToken)
            ?? throw new EstornoImpossivelException($"Orçamento do pagamento {pagamento.Id} não existe.");
        var agora = Agora();

        if (pagamento.Status == StatusPagamento.Estornado)
        {
            LogJaEstornado(comando.NumeroOS, pagamento.Id);
            await _publicador.PublicarAsync(FabricaMensagens.PagamentoEstornado(orcamento, pagamento, agora), cancellationToken);
            await _repositorio.SalvarAsync(cancellationToken);
            return ResultadoProcessamento.Ignorada;
        }

        await _estorno.EstornarAsync(orcamento, pagamento, comando.Motivo, agora, cancellationToken);
        await _repositorio.SalvarAsync(cancellationToken);
        return ResultadoProcessamento.Processada;
    }

    private DateTime Agora() => _relogio.GetUtcNow().UtcDateTime;

    [LoggerMessage(Level = LogLevel.Information, Message = "Orçamento da OS {NumeroOS} já existe ({Status}); pedido ignorado")]
    private partial void LogOrcamentoJaExiste(string numeroOS, StatusOrcamento status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OS {NumeroOS}: total informado {Informado} difere do calculado {Calculado}; vale o calculado")]
    private partial void LogTotalDivergente(string numeroOS, decimal informado, decimal calculado);

    [LoggerMessage(Level = LogLevel.Information, Message = "OS {NumeroOS}: cliente sem e-mail; orçamento enviado sem notificação")]
    private partial void LogClienteSemEmail(string numeroOS);

    [LoggerMessage(Level = LogLevel.Information, Message = "OS {NumeroOS}: pagamento {PagamentoId} já estava estornado; resposta reenviada")]
    private partial void LogJaEstornado(string numeroOS, Guid pagamentoId);
}
