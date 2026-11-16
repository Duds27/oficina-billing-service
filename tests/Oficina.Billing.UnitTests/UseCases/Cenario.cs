using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oficina.Billing.Application;
using Oficina.Billing.Application.UseCases;
using Oficina.Billing.Domain;
using Oficina.Billing.Infrastructure.Pagamentos;
using Oficina.Billing.UnitTests.TestSupport;
using Oficina.Contracts;

namespace Oficina.Billing.UnitTests.UseCases;

/// <summary>Casos de uso com repositório, publicador e gateway em memória.</summary>
internal sealed class Cenario
{
    public static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    public static readonly Guid Correlacao = Guid.Parse("3f2c9a4e-8d1b-4c55-9a7e-2b6f0d9e1a11");

    public Cenario()
    {
        var estorno = new EstornoAutomatico(Gateway, Publicador, NullLogger<EstornoAutomatico>.Instance);
        Comandos = new ComandosSagaUseCases(Repositorio, Publicador, estorno, Relogio,
            Options.Create(new OrcamentoOptions { Validade = TimeSpan.FromHours(48), UrlAprovacao = "https://oficina/orcamentos/{0}" }),
            NullLogger<ComandosSagaUseCases>.Instance);
        Pagamentos = new PagamentosUseCases(Repositorio, Gateway, Publicador, estorno, Relogio, NullLogger<PagamentosUseCases>.Instance);
        Orcamentos = OrcamentosCom(Gateway);
    }

    public OrcamentosUseCases OrcamentosCom(Oficina.Billing.Application.Abstractions.IGatewayPagamento gateway) =>
        new(Repositorio, gateway, Publicador, Relogio, Options.Create(new PagamentosOptions { PrazoPagamento = TimeSpan.FromHours(48) }),
            NullLogger<OrcamentosUseCases>.Instance);

    public RepositorioEmMemoria Repositorio { get; } = new();
    public PublicadorEmMemoria Publicador { get; } = new();
    public Relogio Relogio { get; } = new(T0);
    public FakeGatewayPagamento Gateway { get; } = new();
    public ComandosSagaUseCases Comandos { get; }
    public PagamentosUseCases Pagamentos { get; }
    public OrcamentosUseCases Orcamentos { get; }

    public static GerarOrcamento Gerar(string? email = "maria@example.com") => new()
    {
        CorrelationId = Correlacao,
        OsId = 42,
        NumeroOS = "OS-0042",
        OcorridoEm = T0,
        Cliente = new ClienteInfo { Id = 7, Nome = "Maria", Email = email },
        Itens =
        [
            new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de óleo", Quantidade = 1, PrecoUnitario = 120m },
            new ItemOrcamentoInfo { Tipo = "Peca", ReferenciaId = 15, Descricao = "Filtro", Quantidade = 1, PrecoUnitario = 45.90m }
        ],
        ValorTotal = 165.90m
    };

    public static CancelarOrcamento Cancelar() => new() { CorrelationId = Correlacao, OsId = 42, NumeroOS = "OS-0042", Motivo = "Prazo de aprovação expirado" };

    public Orcamento Orcamento => Repositorio.Orcamentos.Single();

    /// <summary>Orçamento gerado, enviado e aprovado, com o pagamento pendente (como a API deixaria).</summary>
    public async Task<Pagamento> AprovadoComPagamentoPendenteAsync()
    {
        await Comandos.GerarOrcamentoAsync(Gerar());
        Orcamento.Aprovar(Relogio.Avancar(TimeSpan.FromMinutes(1)));
        var pagamento = Pagamento.Criar(Orcamento, Relogio.Agora);
        pagamento.RegistrarCobranca("pref-1", "https://checkout/pref-1");
        Repositorio.Adicionar(pagamento);
        Publicador.Publicadas.Clear();
        return pagamento;
    }

    /// <summary>Simula o pagamento no gateway e processa a notificação, como o webhook faria.</summary>
    public async Task<string> PagarAsync(Pagamento pagamento, bool aprovado = true)
    {
        var mpPaymentId = Gateway.Simular(pagamento.Id, pagamento.Valor, aprovado);
        await Pagamentos.ProcessarNotificacaoAsync(new ProcessarNotificacaoPagamento { MpPaymentId = mpPaymentId, RecebidaEm = Relogio.Agora });
        return mpPaymentId;
    }
}
