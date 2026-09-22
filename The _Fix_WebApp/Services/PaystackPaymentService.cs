using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using The__Fix_WebApp.Services;

namespace FashionFix.Web.Services;

public class PaystackOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
}

public class PaystackPaymentService : IPaymentService
{
    private readonly HttpClient _http;
    private readonly PaystackOptions _options;

    public PaystackPaymentService(HttpClient http, IOptions<PaystackOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.BaseAddress = new Uri("https://api.paystack.co/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.SecretKey);
    }

    public async Task<PaymentInitResult> InitializeTransactionAsync(
        string email, decimal amountRands, string reference, string callbackUrl)
    {
        var payload = new
        {
            email,
            amount = (int)(amountRands * 100),
            reference,
            callback_url = callbackUrl
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("transaction/initialize", content);
        var body = await response.Content.ReadAsStringAsync();


        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode || !root.GetProperty("status").GetBoolean())
        {
            return new PaymentInitResult
            {
                Success = false,
                ErrorMessage = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Payment initialization failed."
            };
        }

        var data = root.GetProperty("data");
        return new PaymentInitResult
        {
            Success = true,
            AuthorizationUrl = data.GetProperty("authorization_url").GetString(),
            Reference = data.GetProperty("reference").GetString()
        };
    }

    public async Task<PaymentVerifyResult> VerifyTransactionAsync(string reference)
    {
        var response = await _http.GetAsync($"transaction/verify/{reference}");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode || !root.GetProperty("status").GetBoolean())
        {
            return new PaymentVerifyResult { Success = false, ErrorMessage = "Verification request failed." };
        }

        var data = root.GetProperty("data");
        var gatewayStatus = data.GetProperty("status").GetString();
        var amountKobo = data.GetProperty("amount").GetInt64();

        return new PaymentVerifyResult
        {
            Success = gatewayStatus == "success",
            Reference = reference,
            AmountRands = amountKobo / 100m,
            ErrorMessage = gatewayStatus == "success" ? null : $"Transaction status: {gatewayStatus}",
            Authorization = gatewayStatus == "success" ? ParseAuthorization(data) : null
        };
    }

    public async Task<PaymentVerifyResult> ChargeAuthorizationAsync(string email, decimal amountRands, string authorizationCode, string reference)
    {
        var payload = new
        {
            email,
            amount = (int)(amountRands * 100),
            authorization_code = authorizationCode,
            reference
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("transaction/charge_authorization", content);
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode || !root.GetProperty("status").GetBoolean())
        {
            return new PaymentVerifyResult
            {
                Success = false,
                ErrorMessage = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Charging the saved card failed."
            };
        }

        var data = root.GetProperty("data");
        var gatewayStatus = data.GetProperty("status").GetString();
        var amountKobo = data.GetProperty("amount").GetInt64();

        return new PaymentVerifyResult
        {
            Success = gatewayStatus == "success",
            Reference = reference,
            AmountRands = amountKobo / 100m,
            ErrorMessage = gatewayStatus == "success" ? null : $"Card was declined: {gatewayStatus}",
            Authorization = gatewayStatus == "success" ? ParseAuthorization(data) : null
        };
    }

    /// <summary>
    /// Pulls Paystack's "authorization" object out of a verify/charge response. Only ever
    /// used to populate PaystackAuthorization for display + the reusable token - raw card
    /// numbers are never present in this payload in the first place (Paystack itself never
    /// sends them back).
    /// </summary>
    private static PaystackAuthorization? ParseAuthorization(JsonElement data)
    {
        if (!data.TryGetProperty("authorization", out var auth) || auth.ValueKind != JsonValueKind.Object)
            return null;

        if (!auth.TryGetProperty("authorization_code", out var codeProp) || string.IsNullOrWhiteSpace(codeProp.GetString()))
            return null;

        return new PaystackAuthorization
        {
            AuthorizationCode = codeProp.GetString()!,
            Last4 = auth.TryGetProperty("last4", out var last4) ? last4.GetString() : null,
            CardType = auth.TryGetProperty("card_type", out var cardType) ? cardType.GetString() : null,
            ExpiryMonth = auth.TryGetProperty("exp_month", out var expMonth) && int.TryParse(expMonth.GetString(), out var em) ? em : null,
            ExpiryYear = auth.TryGetProperty("exp_year", out var expYear) && int.TryParse(expYear.GetString(), out var ey) ? ey : null,
            Bank = auth.TryGetProperty("bank", out var bank) ? bank.GetString() : null,
            Reusable = auth.TryGetProperty("reusable", out var reusable) && reusable.GetBoolean()
        };
    }
}