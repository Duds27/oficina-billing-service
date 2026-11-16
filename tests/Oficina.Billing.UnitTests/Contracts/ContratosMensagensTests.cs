using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MassTransit;
using MassTransit.Serialization;
using Oficina.Contracts;

namespace Oficina.Billing.UnitTests.Contracts;

/// <summary>
/// Testes de contrato (docs/contratos-mensagens.md, seção 6). Quebrar um destes testes
/// significa quebrar o os-service sem perceber.
/// </summary>
public partial class ContratosMensagensTests
{
    private static readonly JsonSerializerOptions Json = SystemTextJsonMessageSerializer.Options;

    private static readonly Guid Correlacao = Guid.Parse("3f2c9a4e-8d1b-4c55-9a7e-2b6f0d9e1a11");
    private static readonly Guid OrcamentoId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000001");
    private static readonly Guid PagamentoId = Guid.Parse("a1b2c3d4-0000-4000-8000-000000000002");
    private static readonly DateTime Ocorrido = new(2026, 11, 15, 14, 3, 27, 512, DateTimeKind.Utc);

    /// <summary>Campos de cada mensagem além do envelope, como no catálogo (seção 2).</summary>
    public static TheoryData<Type, string[]> Catalogo => new()
    {
        { typeof(GerarOrcamento), ["Cliente", "Itens", "ValorTotal"] },
        { typeof(CancelarOrcamento), ["Motivo"] },
        { typeof(EstornarPagamento), ["PagamentoId", "Motivo"] },
        { typeof(OrcamentoGerado), ["OrcamentoId", "ValorTotal", "ValidoAte"] },
        { typeof(OrcamentoAprovado), ["OrcamentoId", "PagamentoId", "LinkPagamento"] },
        { typeof(OrcamentoRecusado), ["OrcamentoId", "Motivo"] },
        { typeof(PagamentoConfirmado), ["PagamentoId", "MpPaymentId", "Valor"] },
        { typeof(PagamentoRecusado), ["PagamentoId", "Motivo"] },
        { typeof(PagamentoEstornado), ["PagamentoId", "Valor"] }
    };

    public static TheoryData<string, object> Exemplos => new()
    {
        { "GerarOrcamento", Envelope(new GerarOrcamento
            {
                Cliente = new ClienteInfo { Id = 7, Nome = "Maria Souza", Email = "maria@example.com" },
                Itens =
                [
                    new ItemOrcamentoInfo { Tipo = "Servico", ReferenciaId = 3, Descricao = "Troca de óleo", Quantidade = 1, PrecoUnitario = 120.00m },
                    new ItemOrcamentoInfo { Tipo = "Peca", ReferenciaId = 15, Descricao = "Filtro de óleo", Quantidade = 1, PrecoUnitario = 45.90m }
                ],
                ValorTotal = 165.90m
            }) },
        { "CancelarOrcamento", Envelope(new CancelarOrcamento { Motivo = "Prazo de aprovação do orçamento expirado" }) },
        { "EstornarPagamento", Envelope(new EstornarPagamento { PagamentoId = PagamentoId, Motivo = "Execução rejeitada: falha simulada" }) },
        { "OrcamentoGerado", Envelope(new OrcamentoGerado { OrcamentoId = OrcamentoId, ValorTotal = 165.90m, ValidoAte = Ocorrido.AddDays(2) }) },
        { "OrcamentoAprovado", Envelope(new OrcamentoAprovado
            {
                OrcamentoId = OrcamentoId,
                PagamentoId = PagamentoId,
                LinkPagamento = "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=123-abc"
            }) },
        { "OrcamentoRecusado", Envelope(new OrcamentoRecusado { OrcamentoId = OrcamentoId, Motivo = "Caro demais" }) },
        { "PagamentoConfirmado", Envelope(new PagamentoConfirmado { PagamentoId = PagamentoId, MpPaymentId = "1234567890", Valor = 165.90m }) },
        { "PagamentoRecusado", Envelope(new PagamentoRecusado { PagamentoId = PagamentoId, Motivo = "cc_rejected_insufficient_amount" }) },
        { "PagamentoEstornado", Envelope(new PagamentoEstornado { PagamentoId = PagamentoId, Valor = 165.90m }) },
        { "ProcessarNotificacaoPagamento", new ProcessarNotificacaoPagamento
            {
                MpPaymentId = "1234567890",
                RequestId = "bb56a2f1-6aae-46ac-982e-9dcd3581d08e",
                RecebidaEm = Ocorrido
            } }
    };

    private static readonly string[] CamposEnvelope = ["CorrelationId", "OsId", "NumeroOS", "OcorridoEm"];

    [Fact]
    public void Catalogo_tem_as_9_mensagens_do_billing_service()
    {
        var mensagens = typeof(MensagemOS).Assembly.GetTypes()
            .Where(t => t.Namespace == "Oficina.Contracts" && !t.IsAbstract && typeof(MensagemOS).IsAssignableFrom(t))
            .ToHashSet();

        var catalogo = Catalogo.Select(linha => (Type)linha[0]).ToHashSet();

        Assert.Equal(9, mensagens.Count);
        Assert.True(catalogo.SetEquals(mensagens), "Mensagem nova ou removida: atualize o catálogo e docs/contratos-mensagens.md.");
    }

    [Theory]
    [MemberData(nameof(Catalogo))]
    public void Mensagem_tem_URN_e_exchange_derivados_do_nome(Type tipo, string[] _)
    {
        var kebab = Kebab(tipo.Name);

        Assert.Equal($"urn:message:oficina:{kebab}:v1", tipo.GetCustomAttribute<MessageUrnAttribute>()!.Urn.ToString());
        Assert.Equal($"oficina.{kebab}.v1", tipo.GetCustomAttribute<EntityNameAttribute>()!.EntityName);
    }

    [Fact]
    public void Evento_interno_tem_URN_proprio_do_billing()
    {
        var tipo = typeof(ProcessarNotificacaoPagamento);

        Assert.Equal("urn:message:oficina:billing-processar-notificacao-pagamento:v1", tipo.GetCustomAttribute<MessageUrnAttribute>()!.Urn.ToString());
        Assert.Equal("oficina.billing-processar-notificacao-pagamento.v1", tipo.GetCustomAttribute<EntityNameAttribute>()!.EntityName);
        Assert.False(typeof(MensagemOS).IsAssignableFrom(tipo));
    }

    [Theory]
    [MemberData(nameof(Catalogo))]
    public void Mensagem_tem_exatamente_os_campos_do_catalogo(Type tipo, string[] campos)
    {
        var propriedades = tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n != "EqualityContract")
            .Order()
            .ToArray();

        Assert.Equal(CamposEnvelope.Concat(campos).Order().ToArray(), propriedades);
    }

    [Fact]
    public void Envelope_nao_vira_exchange()
    {
        Assert.NotNull(typeof(MensagemOS).GetCustomAttribute<ExcludeFromTopologyAttribute>());
        Assert.NotNull(typeof(IMensagemOS).GetCustomAttribute<ExcludeFromTopologyAttribute>());
    }

    [Theory]
    [MemberData(nameof(Exemplos))]
    public void Serializacao_bate_com_o_snapshot(string nome, object exemplo)
    {
        var esperado = JsonNode.Parse(Snapshot(nome));
        var atual = JsonSerializer.SerializeToNode(exemplo, exemplo.GetType(), Json);

        Assert.True(JsonNode.DeepEquals(esperado, atual),
            $"{nome} mudou no fio. Atual:{Environment.NewLine}{atual!.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
    }

    [Theory]
    [MemberData(nameof(Exemplos))]
    public void Snapshot_faz_ida_e_volta_sem_perder_campo(string nome, object exemplo)
    {
        var lido = JsonSerializer.Deserialize(Snapshot(nome), exemplo.GetType(), Json)!;

        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(exemplo, exemplo.GetType(), Json),
            JsonSerializer.SerializeToNode(lido, exemplo.GetType(), Json)));
    }

    [Fact]
    public void GerarOrcamento_do_exemplo_da_secao_2_2_desserializa_todos_os_campos()
    {
        // docs/contratos-mensagens.md, seção 2.2 (decimais como número; no fio vão como texto).
        const string exemplo = """
            {
              "correlationId": "3f2c9a4e-8d1b-4c55-9a7e-2b6f0d9e1a11",
              "osId": 42,
              "numeroOS": "OS-20261115-0042",
              "ocorridoEm": "2026-11-15T14:03:27.512Z",
              "cliente": { "id": 7, "nome": "Maria Souza", "email": "maria@example.com" },
              "itens": [
                { "tipo": "Servico", "referenciaId": 3, "descricao": "Troca de óleo", "quantidade": 1, "precoUnitario": 120.00 },
                { "tipo": "Peca", "referenciaId": 15, "descricao": "Filtro de óleo", "quantidade": 1, "precoUnitario": 45.90 }
              ],
              "valorTotal": 165.90
            }
            """;

        var msg = JsonSerializer.Deserialize<GerarOrcamento>(exemplo, Json)!;

        Assert.Equal(Correlacao, msg.CorrelationId);
        Assert.Equal(new ClienteInfo { Id = 7, Nome = "Maria Souza", Email = "maria@example.com" }, msg.Cliente);
        Assert.Equal(new ItemOrcamentoInfo { Tipo = "Peca", ReferenciaId = 15, Descricao = "Filtro de óleo", Quantidade = 1, PrecoUnitario = 45.90m }, msg.Itens[1]);
        Assert.Equal(165.90m, msg.ValorTotal);
    }

    private static T Envelope<T>(T mensagem) where T : MensagemOS => mensagem with
    {
        CorrelationId = Correlacao,
        OsId = 42,
        NumeroOS = "OS-20261115-0042",
        OcorridoEm = Ocorrido
    };

    private static string Snapshot(string nome) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", $"{nome}.json"));

    private static string Kebab(string nome) => MaiusculaInterna().Replace(nome, "-$1").ToLowerInvariant();

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex MaiusculaInterna();
}
