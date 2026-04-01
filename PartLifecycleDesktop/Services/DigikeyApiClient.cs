using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Text.Json;
using PartLifecycleDesktop.Models;

namespace PartLifecycleDesktop.Services;

public sealed class DigikeyApiClient
{
    private static readonly HttpClient HttpClient = CreateClient();
    private readonly DigikeyApiSettings? _settings;
    private AccessTokenState? _tokenState;

    public DigikeyApiClient()
    {
        _settings = DigikeyApiSettings.Load();
    }

    public bool IsConfigured => _settings is not null;

    public async Task<ApiLifecycleResult?> TryAnalyzePartAsync(string partNumber, CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return null;
        }

        var token = await GetAccessTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_settings.ApiBaseUrl}/products/v4/search/{Uri.EscapeDataString(partNumber)}/productdetails");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-DIGIKEY-Client-Id", _settings.ClientId);
        request.Headers.Add("X-DIGIKEY-Locale-Site", _settings.Site);
        request.Headers.Add("X-DIGIKEY-Locale-Language", _settings.Language);
        request.Headers.Add("X-DIGIKEY-Locale-Currency", _settings.Currency);

        if (!string.IsNullOrWhiteSpace(_settings.AccountId))
        {
            request.Headers.Add("X-DIGIKEY-Account-Id", _settings.AccountId);
        }

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var product = document.RootElement.TryGetProperty("Product", out var productElement)
            ? productElement
            : document.RootElement;

        var manufacturer = TryGetString(product, "Manufacturer", "Name") ?? "Unknown";
        var productUrl = TryGetString(product, "ProductUrl") ?? $"https://www.digikey.com/en/products/result?keywords={Uri.EscapeDataString(partNumber)}";
        var status = ResolveStatus(product);
        var statusSnippet = BuildSnippet(product, status);
        var productSummary = BuildProductSummary(product);

        return new ApiLifecycleResult(
            SourceName: "DigiKey API",
            Url: productUrl,
            Manufacturer: manufacturer,
            Status: status,
            Snippet: statusSnippet,
            ProductSummary: productSummary);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_tokenState is not null && _tokenState.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _tokenState.AccessToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_settings!.ApiBaseUrl}/v1/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _settings.ClientId,
                ["client_secret"] = _settings.ClientSecret,
                ["grant_type"] = "client_credentials"
            })
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var accessToken = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("DigiKey token response did not include an access token.");
        var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
            ? expiresElement.GetInt32()
            : 600;

        _tokenState = new AccessTokenState(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        return accessToken;
    }

    private static string ResolveStatus(JsonElement product)
    {
        var statusText = TryGetString(product, "ProductStatus", "Status") ??
                         TryGetString(product, "ProductStatus") ??
                         string.Empty;

        if (TryGetBoolean(product, "EndOfLife") == true ||
            TryGetBoolean(product, "Discontinued") == true)
        {
            return "Obsolete";
        }

        var normalized = statusText.Trim();
        if (normalized.Contains("Obsolete", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Discontinued", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Last Time Buy", StringComparison.OrdinalIgnoreCase))
        {
            return "Obsolete";
        }

        if (normalized.Contains("Not For New Designs", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("NRND", StringComparison.OrdinalIgnoreCase))
        {
            return "NRND";
        }

        if (normalized.Contains("Active", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        return "Unknown";
    }

    private static string BuildSnippet(JsonElement product, string status)
    {
        var statusText = TryGetString(product, "ProductStatus", "Status") ??
                         TryGetString(product, "ProductStatus") ??
                         status;
        var description = TryGetString(product, "Description", "DetailedDescription") ??
                          TryGetString(product, "Description", "ProductDescription") ??
                          string.Empty;

        return string.IsNullOrWhiteSpace(description)
            ? $"DigiKey API status: {statusText}"
            : $"DigiKey API status: {statusText}. {description}";
    }

    private static string? BuildProductSummary(JsonElement product)
    {
        var description = TryGetString(product, "Description", "ProductDescription") ??
                          TryGetString(product, "Description", "DetailedDescription");
        var series = TryGetString(product, "Series", "Name") ??
                     TryGetString(product, "Series");
        var package = TryGetString(product, "PackageType", "Name") ??
                      TryGetString(product, "PackageType");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(series))
        {
            parts.Add($"Series: {series.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(package))
        {
            parts.Add($"Package: {package.Trim()}");
        }

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    private static string? TryGetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }

    private static bool? TryGetBoolean(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.True || current.ValueKind == JsonValueKind.False
            ? current.GetBoolean()
            : null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private sealed record AccessTokenState(string AccessToken, DateTimeOffset ExpiresAtUtc);
}

public sealed record ApiLifecycleResult(
    string SourceName,
    string Url,
    string Manufacturer,
    string Status,
    string Snippet,
    string? ProductSummary);

public sealed class DigikeyApiSettings
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string? AccountId { get; init; }
    public string Site { get; init; } = "US";
    public string Language { get; init; } = "en";
    public string Currency { get; init; } = "USD";
    public bool UseSandbox { get; init; }

    public string ApiBaseUrl => UseSandbox ? "https://sandbox-api.digikey.com" : "https://api.digikey.com";

    public static DigikeyApiSettings? Load()
    {
        var fromFile = LoadFromFile();
        if (fromFile is not null)
        {
            return fromFile;
        }

        var clientId = Environment.GetEnvironmentVariable("DIGIKEY_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("DIGIKEY_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        return new DigikeyApiSettings
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            AccountId = Environment.GetEnvironmentVariable("DIGIKEY_ACCOUNT_ID"),
            Site = Environment.GetEnvironmentVariable("DIGIKEY_SITE") ?? "US",
            Language = Environment.GetEnvironmentVariable("DIGIKEY_LANGUAGE") ?? "en",
            Currency = Environment.GetEnvironmentVariable("DIGIKEY_CURRENCY") ?? "USD",
            UseSandbox = bool.TryParse(Environment.GetEnvironmentVariable("DIGIKEY_USE_SANDBOX"), out var useSandbox) && useSandbox
        };
    }

    private static DigikeyApiSettings? LoadFromFile()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "digikey-api.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<DigikeyApiSettings>(json);
    }
}
