using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Domain;
using Oficina.Contracts;

namespace Oficina.Billing.UnitTests.UseCases;

public class OrcamentosUseCasesTests
{
    private static readonly Solicitante Maria = new(DaOficina: false, Atendente: false, ClienteId: 7);
    private static readonly Solicitante OutroCliente = new(false, false, 8);
    private static readonly Solicitante Atendente = new(true, true, null);
    private static readonly Solicitante Mecanico = new(true, false, null);

    private readonly Cenario _c = new();

    private async Task<Orcamento> EnviadoAsync()
    {
        await _c.Comandos.GerarOrcamentoAsync(Cenario.Gerar());
        _c.Publicador.Publicadas.Clear();
        return _c.Orcamento;
    }

    [Fact]
    public async Task Cliente_aprova_o_proprio_orcamento_e_recebe_o_link_de_pagamento()
    {
        var orcamento = await EnviadoAsync();

        var resultado = await _c.Orcamentos.DecidirAsync(orcamento.Id, aprovado: true, null, Maria);

        Assert.Equal(StatusResultado.Ok, resultado.Status);
        Assert.Equal("Aprovado", resultado.Valor!.Status);
        var pagamento = Assert.Single(_c.Repositorio.Pagamentos);
        Assert.Equal(StatusPagamento.Pendente, pagamento.Status);
        Assert.Equal($"https://pagamento.fake.local/checkout/{pagamento.Id}", resultado.Valor.Pagamento!.LinkPagamento);

        var evento = Assert.IsType<OrcamentoAprovado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal(pagamento.Id, evento.PagamentoId);
        Assert.Equal(pagamento.LinkPagamento, evento.LinkPagamento);
        Assert.Equal(orcamento.Id, evento.OrcamentoId);
    }

    [Fact]
    public async Task Recusa_publica_OrcamentoRecusado_com_o_motivo()
    {
        var orcamento = await EnviadoAsync();

        var resultado = await _c.Orcamentos.DecidirAsync(orcamento.Id, aprovado: false, "caro demais", Maria);

        Assert.Equal("Recusado", resultado.Valor!.Status);
        var evento = Assert.IsType<OrcamentoRecusado>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal("caro demais", evento.Motivo);
        Assert.Empty(_c.Repositorio.Pagamentos);
    }

    [Fact]
    public async Task Um_cliente_nao_aprova_nem_ve_o_orcamento_de_outro()
    {
        var orcamento = await EnviadoAsync();

        Assert.Equal(StatusResultado.Proibido, (await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, OutroCliente)).Status);
        Assert.Equal(StatusResultado.Proibido, (await _c.Orcamentos.ObterAsync(orcamento.Id, OutroCliente)).Status);
        Assert.Empty(await _c.Orcamentos.ListarDaOsAsync(42, OutroCliente));
        Assert.Equal(StatusOrcamento.Enviado, orcamento.Status);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Atendente_registra_a_decisao_e_mecanico_so_consulta()
    {
        var orcamento = await EnviadoAsync();

        Assert.Equal(StatusResultado.Proibido, (await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, Mecanico)).Status);
        Assert.Equal(StatusResultado.Ok, (await _c.Orcamentos.ObterAsync(orcamento.Id, Mecanico)).Status);
        Assert.Equal(StatusResultado.Ok, (await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, Atendente)).Status);
    }

    [Fact]
    public async Task Orcamento_vencido_fica_expirado_e_nao_pode_ser_aprovado()
    {
        var orcamento = await EnviadoAsync();
        _c.Relogio.Avancar(TimeSpan.FromHours(49));

        var resultado = await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, Maria);

        Assert.Equal(StatusResultado.Conflito, resultado.Status);
        Assert.Contains("venceu", resultado.Erro, StringComparison.Ordinal);
        Assert.Equal(StatusOrcamento.Expirado, orcamento.Status);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Decidir_de_novo_e_conflito()
    {
        var orcamento = await EnviadoAsync();
        await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, Maria);

        var resultado = await _c.Orcamentos.DecidirAsync(orcamento.Id, false, null, Maria);

        Assert.Equal(StatusResultado.Conflito, resultado.Status);
        Assert.Single(_c.Repositorio.Pagamentos);
    }

    [Fact]
    public async Task Gateway_fora_do_ar_responde_indisponivel_sem_gravar_nada()
    {
        var orcamento = await EnviadoAsync();
        var gravacoes = _c.Repositorio.Gravacoes;

        var resultado = await _c.OrcamentosCom(new GatewayForaDoAr()).DecidirAsync(orcamento.Id, true, null, Maria);

        Assert.Equal(StatusResultado.Indisponivel, resultado.Status);
        Assert.Equal(gravacoes, _c.Repositorio.Gravacoes);
        Assert.Empty(_c.Repositorio.Pagamentos);
        Assert.Empty(_c.Publicador.Publicadas);
    }

    [Fact]
    public async Task Orcamento_inexistente_responde_nao_encontrado()
    {
        Assert.Equal(StatusResultado.NaoEncontrado, (await _c.Orcamentos.ObterAsync(Guid.NewGuid(), Atendente)).Status);
        Assert.Equal(StatusResultado.NaoEncontrado, (await _c.Orcamentos.DecidirAsync(Guid.NewGuid(), true, null, Atendente)).Status);
        Assert.Equal(StatusResultado.NaoEncontrado, (await _c.Orcamentos.ObterPagamentoAsync(Guid.NewGuid(), Atendente)).Status);
    }

    [Fact]
    public async Task Consulta_traz_o_pagamento_e_respeita_o_dono()
    {
        var orcamento = await EnviadoAsync();
        await _c.Orcamentos.DecidirAsync(orcamento.Id, true, null, Maria);
        var pagamento = _c.Repositorio.Pagamentos.Single();

        var lista = await _c.Orcamentos.ListarDaOsAsync(42, Maria);

        Assert.Equal(pagamento.Id, Assert.Single(lista).Pagamento!.Id);
        Assert.Equal("Pendente", (await _c.Orcamentos.ObterPagamentoAsync(pagamento.Id, Maria)).Valor!.Status);
        Assert.Equal(StatusResultado.Proibido, (await _c.Orcamentos.ObterPagamentoAsync(pagamento.Id, OutroCliente)).Status);
    }

    [Fact]
    public async Task Notificacao_registrada_vai_para_o_outbox()
    {
        await _c.Orcamentos.RegistrarNotificacaoAsync("mp-1", "req-1");

        var mensagem = Assert.IsType<ProcessarNotificacaoPagamento>(Assert.Single(_c.Publicador.Publicadas));
        Assert.Equal("mp-1", mensagem.MpPaymentId);
        Assert.Equal("req-1", mensagem.RequestId);
        Assert.Equal(1, _c.Repositorio.Gravacoes);
    }

    private sealed class GatewayForaDoAr : IGatewayPagamento
    {
        public Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default) =>
            throw new GatewayIndisponivelException("timeout");

        public Task<SituacaoPagamento?> ConsultarPagamentoAsync(string mpPaymentId, CancellationToken cancellationToken = default) =>
            throw new GatewayIndisponivelException("timeout");

        public Task<EstornoRealizado> EstornarAsync(string mpPaymentId, Guid chaveIdempotencia, CancellationToken cancellationToken = default) =>
            throw new GatewayIndisponivelException("timeout");
    }
}
