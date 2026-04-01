using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PartLifecycleDesktop.Services;

public sealed class MouserApiClient
{
    private static readonly HttpClient HttpClient = CreateClient();
    private readonly MouserApiSettings? _settings;

    public MouserApiClient()
    {
        _settings = MouserApiSettings.Load();
    }

    public bool IsConfigured => _settings is not null;

    public async Task<ApiLifecycleResult?> TryAnalyzePartAsync(string partNumber, CancellationToken cancellationToken)
    {
        if (_settings is null)
        {
            return null;
        }

        var requestUrl = $"{_settings.ApiBaseUrl}/api/v1.0/search/partnumber?apiKey={Uri.EscapeDataString(_settings.ApiKey)}";

        var payload = new
        {
            SearchByPartRequest = new
            {
                MouserPartNumber = partNumber,
                PartSearchOptions = _settings.PartSearchOptions
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var root = document.RootElement;
        if (root.TryGetProperty("Errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array &&
            errorsElement.GetArrayLength() > 0)
        {
            var firstError = errorsElement[0];
            var code = TryGetString(firstError, "Code") ?? "Unknown";
            var message = TryGetString(firstError, "Message") ?? "Mouser API request failed.";
            throw new InvalidOperationException($"{code}: {message}");
        }

        if (!root.TryGetProperty("SearchResults", out var searchResults) ||
            !searchResults.TryGetProperty("Parts", out var partsElement) ||
            partsElement.ValueKind != JsonValueKind.Array ||
            partsElement.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement? bestPart = null;
        foreach (var part in partsElement.EnumerateArray())
        {
            var manufacturerPartNumber = TryGetString(part, "ManufacturerPartNumber");
            if (!string.IsNullOrWhiteSpace(manufacturerPartNumber) &&
                manufacturerPartNumber.Equals(partNumber, StringComparison.OrdinalIgnoreCase))
            {
                bestPart = part;
                break;
            }

            bestPart ??= part;
        }

        if (bestPart is null)
        {
            return null;
        }

        var element = bestPart.Value;
        var manufacturer = TryGetString(element, "Manufacturer") ??
                           TryGetString(element, "ManufacturerName") ??
                           "Unknown";
        var productUrl = TryGetString(element, "ProductDetailUrl") ??
                         TryGetString(element, "ProductDetailUrl2") ??
                         $"https://www.mouser.com/c/?q={Uri.EscapeDataString(partNumber)}";
        var status = ResolveStatus(element);
        var snippet = BuildSnippet(element, status);
        var productSummary = BuildProductSummary(element);

        return new ApiLifecycleResult(
            SourceName: "Mouser API",
            Url: productUrl,
            Manufacturer: manufacturer,
            Status: status,
            Snippet: snippet,
            ProductSummary: productSummary);
    }

    private static string ResolveStatus(JsonElement part)
    {
        var lifecycle = TryGetString(part, "LifecycleStatus") ?? string.Empty;
        var availability = TryGetString(part, "Availability") ?? string.Empty;
        var replacement = TryGetString(part, "SuggestedReplacement") ?? string.Empty;
        var normalized = $"{lifecycle} {availability} {replacement}".Trim();

        if (normalized.Contains("Obsolete", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Discontinued", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Replacement", StringComparison.OrdinalIgnoreCase))
        {
            return "Obsolete";
        }

        if (normalized.Contains("Not Recommended for New Designs", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("NRND", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("LifeBuy", StringComparison.OrdinalIgnoreCase))
        {
            return "NRND";
        }

        if (normalized.Contains("Active", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("In Stock", StringComparison.OrdinalIgnoreCase))
        {
            return "Active";
        }

        return "Unknown";
    }

    private static string BuildSnippet(JsonElement part, string status)
    {
        var lifecycle = TryGetString(part, "LifecycleStatus") ?? status;
        var description = TryGetString(part, "Description") ?? string.Empty;
        var replacement = TryGetString(part, "SuggestedReplacement") ?? string.Empty;

        var pieces = new List<string> { $"Mouser API lifecycle: {lifecycle}" };
        if (!string.IsNullOrWhiteSpace(description))
        {
            pieces.Add(description);
        }

        if (!string.IsNullOrWhiteSpace(replacement))
        {
            pieces.Add($"Suggested replacement: {replacement}");
        }

        return string.Join(". ", pieces);
    }

    private static string? BuildProductSummary(JsonElement part)
    {
        var description = TryGetString(part, "Description");
        var category = TryGetString(part, "Category");
        var package = TryGetString(part, "Packaging");

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description.Trim());
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            parts.Add($"Category: {category.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(package))
        {
            parts.Add($"Packaging: {package.Trim()}");
        }

        return parts.Count == 0 ? null : string.Join(". ", parts);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
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
}

public sealed class MouserApiSettings
{
    public string ApiKey { get; init; } = string.Empty;
    public string PartSearchOptions { get; init; } = "None";
    public bool UseSandbox { get; init; }

    public string ApiBaseUrl => UseSandbox ? "https://api.mouser.com" : "https://api.mouser.com";

    public static MouserApiSettings? Load()
    {
        var fromFile = LoadFromFile();
        if (fromFile is not null)
        {
            return fromFile;
        }

        var apiKey = Environment.GetEnvironmentVariable("MOUSER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        return new MouserApiSettings
        {
            ApiKey = apiKey,
            PartSearchOptions = Environment.GetEnvironmentVariable("MOUSER_PART_SEARCH_OPTIONS") ?? "None",
            UseSandbox = false
        };
    }

    private static MouserApiSettings? LoadFromFile()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "mouser-api.json");
        if (!File.Exists(filePath))
        {
            return null;
        }

        var json = File.ReadAllText(filePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<MouserApiSettings>(json);
    }
}
