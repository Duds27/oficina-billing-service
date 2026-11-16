using Microsoft.EntityFrameworkCore;
using Oficina.Billing.Application.Abstractions;
using Oficina.Billing.Domain;
using Oficina.Billing.Infrastructure.Persistence;
using Oficina.Billing.IntegrationTests.Infraestrutura;

namespace Oficina.Billing.IntegrationTests.Persistence;

[Collection(ColecaoPostgres.Nome)]
public sealed class RepositorioPostgresTests : IAsyncLifetime
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly PostgresFixture _postgres;
    private string _banco = null!;

    public RepositorioPostgresTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _banco = _postgres.NovoBanco();
        await using var db = await _postgres.NovoContextoMigradoAsync(_banco);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private (BillingDbContext Db, BillingRepositorio Repositorio) Novo()
    {
        var db = PostgresFixture.Contexto(_banco);
        return (db, new BillingRepositorio(db));
    }

    private static Orcamento Orcamento(int osId = 42, Guid? correlacao = null) =>
        Domain.Orcamento.Gerar(correlacao ?? Guid.NewGuid(), osId, $"OS-{osId:D4}", new ClienteOrcamento(7, "Maria", "maria@example.com"),
            [new ItemOrcamento("Servico", 3, "Troca de óleo", 1, 120m), new ItemOrcamento("Peca", 15, "Filtro", 1.5m, 45.90m)],
            TimeSpan.FromHours(48), T0);

    [Fact]
    public async Task Grava_e_le_orcamento_com_cliente_itens_e_pagamento()
    {
        var orcamento = Orcamento();
        orcamento.MarcarEnviado(T0);
        orcamento.Aprovar(T0.AddMinutes(1));
        var pagamento = Pagamento.Criar(orcamento, T0.AddMinutes(1));
        pagamento.RegistrarCobranca("pref-1", "https://mp/pref-1");
        var notificacao = Notificacao.DoOrcamento(orcamento, "https://oficina/orcamentos/x", T0)!;

        var (db, repositorio) = Novo();
        await using (db)
        {
            repositorio.Adicionar(orcamento);
            repositorio.Adicionar(pagamento);
            repositorio.Adicionar(notificacao);
            await repositorio.SalvarAsync();
        }

        var (leitura, lido) = Novo();
        await using (leitura)
        {
            var o = (await lido.ObterOrcamentoAsync(orcamento.Id))!;
            Assert.Equal(StatusOrcamento.Aprovado, o.Status);
            Assert.Equal(188.85m, o.ValorTotal);
            Assert.Equal("Maria", o.Cliente.Nome);
            Assert.Equal("maria@example.com", o.Cliente.Email);
            Assert.Equal(["Troca de óleo", "Filtro"], o.Itens.Select(i => i.Descricao));
            Assert.Equal(1.5m, o.Itens[1].Quantidade);
            Assert.Equal(DateTimeKind.Utc, o.ValidoAte.Kind);
            Assert.NotEqual(0u, o.Versao);

            Assert.Equal(o.Id, (await lido.ObterOrcamentoDaSagaAsync(orcamento.CorrelationId))!.Id);
            var p = (await lido.ObterPagamentoDoOrcamentoAsync(orcamento.Id))!;
            Assert.Equal(pagamento.Id, p.Id);
            Assert.Equal("https://mp/pref-1", p.LinkPagamento);
            Assert.Equal(StatusPagamento.Pendente, (await lido.ObterPagamentoAsync(pagamento.Id))!.Status);
            Assert.Equal(1, await leitura.Notificacoes.CountAsync(n => n.EnviadaEm == null));
        }
    }

    [Fact]
    public async Task Duas_gravacoes_do_mesmo_orcamento_dao_conflito()
    {
        var orcamento = Orcamento();
        var (db, repositorio) = Novo();
        await using (db)
        {
            repositorio.Adicionar(orcamento);
            await repositorio.SalvarAsync();
        }

        var (db1, r1) = Novo();
        var (db2, r2) = Novo();
        await using (db1)
        await using (db2)
        {
            var a = (await r1.ObterOrcamentoAsync(orcamento.Id))!;
            var b = (await r2.ObterOrcamentoAsync(orcamento.Id))!;
            a.MarcarEnviado(T0);
            await r1.SalvarAsync();

            b.Cancelar("ao mesmo tempo", T0);
            await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => r2.SalvarAsync());
        }
    }

    [Fact]
    public async Task Dois_orcamentos_da_mesma_saga_dao_conflito()
    {
        var correlacao = Guid.NewGuid();
        var (db1, r1) = Novo();
        await using (db1)
        {
            r1.Adicionar(Orcamento(correlacao: correlacao));
            await r1.SalvarAsync();
        }

        var (db2, r2) = Novo();
        await using (db2)
        {
            r2.Adicionar(Orcamento(correlacao: correlacao));
            await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => r2.SalvarAsync());
        }
    }

    [Fact]
    public async Task Lista_os_orcamentos_da_OS_do_mais_novo_para_o_mais_antigo()
    {
        var antigo = Orcamento(10);
        var outro = Orcamento(11);
        var (db, repositorio) = Novo();
        await using (db)
        {
            repositorio.Adicionar(antigo);
            repositorio.Adicionar(outro);
            repositorio.Adicionar(Domain.Orcamento.RegistrarCancelamentoAntecipado(Guid.NewGuid(), 10, "OS-0010", null, T0.AddHours(1)));
            await repositorio.SalvarAsync();
        }

        var (leitura, lido) = Novo();
        await using (leitura)
        {
            var daOs = await lido.ListarOrcamentosDaOsAsync(10);
            Assert.Equal(2, daOs.Count);
            Assert.Equal(StatusOrcamento.Cancelado, daOs[0].Status);
            Assert.Equal(antigo.Id, daOs[1].Id);
            Assert.Empty(await lido.ListarOrcamentosDaOsAsync(99));
        }
    }

    [Fact]
    public async Task Mesmo_pagamento_do_Mercado_Pago_nao_confirma_duas_cobrancas()
    {
        var (db, repositorio) = Novo();
        await using (db)
        {
            foreach (var os in new[] { 20, 21 })
            {
                var o = Orcamento(os);
                o.MarcarEnviado(T0);
                o.Aprovar(T0);
                var p = Pagamento.Criar(o, T0);
                p.Confirmar("mp-123", T0);
                repositorio.Adicionar(o);
                repositorio.Adicionar(p);
            }

            await Assert.ThrowsAsync<ConflitoDeConcorrenciaException>(() => repositorio.SalvarAsync());
        }
    }
}
