using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Oficina.Billing.Application.Abstractions;

namespace Oficina.Billing.Infrastructure.Pagamentos;

/// <summary>
/// Adapter do Mercado Pago (Checkout Pro). O <see cref="HttpClient"/> tipado tem timeout, retry e
/// circuit breaker (<c>Microsoft.Extensions.Http.Resilience</c>). As chamadas que criam algo levam
/// <c>X-Idempotency-Key</c>, então o retry não duplica cobrança nem estorno.
/// </summary>
internal sealed class MercadoPagoGateway : IGatewayPagamento
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly MercadoPagoOptions _opcoes;

    public MercadoPagoGateway(HttpClient http, IOptions<MercadoPagoOptions> opcoes)
    {
        _http = http;
        _opcoes = opcoes.Value;
    }

    public async Task<CobrancaCriada> CriarCobrancaAsync(NovaCobranca cobranca, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cobranca);

        // Um item só, com o total: o Checkout Pro exige quantidade inteira, e o orçamento tem
        // quantidades decimais (horas de serviço, litros).
        var preferencia = new PreferenciaRequest(
            Items: [new ItemRequest(cobranca.PagamentoId.ToString(), $"Orçamento da OS {cobranca.NumeroOS}", 1, "BRL", cobranca.Valor)],
            ExternalReference: cobranca.PagamentoId.ToString(),
            NotificationUrl: _opcoes.NotificationUrl?.ToString(),
            Payer: string.IsNullOrWhiteSpace(cobranca.EmailPagador) ? null : new PayerRequest(cobranca.EmailPagador),
            Expires: true,
            ExpirationDateTo: cobranca.ExpiraEm.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'+00:00'", CultureInfo.InvariantCulture));

        using var requisicao = Requisicao(HttpMethod.Post, "checkout/preferences", cobranca.PagamentoId);
        requisicao.Content = JsonContent.Create(preferencia, options: Json);
        using var resposta = await EnviarAsync(requisicao, "criar a preferência", cancellationToken);

        var criada = await LerAsync<PreferenciaResponse>(resposta, cancellationToken);
        var link = _opcoes.UsarLinkSandbox && !string.IsNullOrEmpty(criada.SandboxInitPoint) ? criada.SandboxInitPoint : criada.InitPoint;
        if (string.IsNullOrEmpty(criada.Id) || string.IsNullOrEmpty(link))
            throw new GatewayIndisponivelException("O Mercado Pago criou a preferência sem id ou sem link.");
        return new CobrancaCriada(criada.Id, link);
    }

    public async Task<SituacaoPagamento?> ConsultarPagamentoAsync(string mpPaymentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        using var requisicao = Requisicao(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(mpPaymentId)}", chaveIdempotencia: null);
        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.SendAsync(requisicao, cancellationToken);
        }
        catch (Exception ex) when (Transitoria(ex, cancellationToken))
        {
            throw new GatewayIndisponivelException("O Mercado Pago não respondeu ao consultar o pagamento.", ex);
        }

        using var _ = resposta;
        if (resposta.StatusCode == HttpStatusCode.NotFound)
            return null;
        Garantir(resposta, "consultar o pagamento");

        var pagamento = await LerAsync<PagamentoResponse>(resposta, cancellationToken);
        Guid? nosso = Guid.TryParse(pagamento.ExternalReference, out var id) ? id : null;
        return new SituacaoPagamento(mpPaymentId, nosso, Traduzir(pagamento.Status), pagamento.TransactionAmount, pagamento.StatusDetail);
    }

    public async Task<EstornoRealizado> EstornarAsync(string mpPaymentId, Guid chaveIdempotencia, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mpPaymentId);

        using var requisicao = Requisicao(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(mpPaymentId)}/refunds", chaveIdempotencia);
        // Corpo vazio: estorno do valor total.
        requisicao.Content = JsonContent.Create(new { }, options: Json);
        using var resposta = await EnviarAsync(requisicao, "estornar o pagamento", cancellationToken);

        var estorno = await LerAsync<EstornoResponse>(resposta, cancellationToken);
        return new EstornoRealizado(estorno.Id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <c>approved</c> → aprovado; <c>rejected</c>/<c>cancelled</c> → recusado;
    /// <c>refunded</c>/<c>charged_back</c> → estornado; o resto (<c>pending</c>, <c>in_process</c>...) aguarda.
    /// </summary>
    internal static StatusNoGateway Traduzir(string? status) => status switch
    {
        "approved" => StatusNoGateway.Aprovado,
        "rejected" or "cancelled" => StatusNoGateway.Recusado,
        "refunded" or "charged_back" => StatusNoGateway.Estornado,
        _ => StatusNoGateway.Pendente
    };

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho, Guid? chaveIdempotencia)
    {
        var requisicao = new HttpRequestMessage(metodo, new Uri(_opcoes.BaseUrl, caminho));
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.AccessToken);
        if (chaveIdempotencia is { } chave)
            requisicao.Headers.Add("X-Idempotency-Key", chave.ToString());
        return requisicao;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage requisicao, string acao, CancellationToken cancellationToken)
    {
        HttpResponseMessage resposta;
        try
        {
            resposta = await _http.SendAsync(requisicao, cancellationToken);
        }
        catch (Exception ex) when (Transitoria(ex, cancellationToken))
        {
            throw new GatewayIndisponivelException($"O Mercado Pago não respondeu ao {acao}.", ex);
        }

        Garantir(resposta, acao);
        return resposta;
    }

    /// <summary>
    /// Falha de rede, timeout ou o resilience handler desistindo (tempo total esgotado ou circuit
    /// breaker aberto: <see cref="Polly.ExecutionRejectedException"/>). Cancelamento de quem chamou não conta.
    /// </summary>
    private static bool Transitoria(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested
        && ex is HttpRequestException or TimeoutException or TaskCanceledException or Polly.ExecutionRejectedException;

    private static void Garantir(HttpResponseMessage resposta, string acao)
    {
        if (resposta.IsSuccessStatusCode)
            return;

        var codigo = (int)resposta.StatusCode;
        resposta.Dispose();
        if (codigo is >= 400 and < 500 && codigo != 408 && codigo != 429)
            throw new GatewayRecusouException($"O Mercado Pago recusou {acao} (HTTP {codigo}).");
        throw new GatewayIndisponivelException($"O Mercado Pago falhou ao {acao} (HTTP {codigo}).");
    }

    private static async Task<T> LerAsync<T>(HttpResponseMessage resposta, CancellationToken cancellationToken) =>
        await resposta.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new GatewayIndisponivelException($"Resposta vazia do Mercado Pago ({typeof(T).Name}).");

    private sealed record PreferenciaRequest(
        IReadOnlyList<ItemRequest> Items,
        string ExternalReference,
        string? NotificationUrl,
        PayerRequest? Payer,
        bool Expires,
        string ExpirationDateTo);

    private sealed record ItemRequest(string Id, string Title, int Quantity, string CurrencyId, decimal UnitPrice);

    private sealed record PayerRequest(string Email);

    private sealed record PreferenciaResponse(string? Id, string? InitPoint, string? SandboxInitPoint);

    private sealed record PagamentoResponse(long Id, string? Status, string? StatusDetail, string? ExternalReference, decimal TransactionAmount);

    private sealed record EstornoResponse(long Id, string? Status);
}
