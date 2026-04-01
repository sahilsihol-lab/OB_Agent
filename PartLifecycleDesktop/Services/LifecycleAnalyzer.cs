using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using PartLifecycleDesktop.Models;

namespace PartLifecycleDesktop.Services;

public sealed class LifecycleAnalyzer
{
    private static readonly HttpClient HttpClient = CreateClient();
    private readonly DigikeyApiClient _digikeyApiClient = new();
    private readonly MouserApiClient _mouserApiClient = new();

    private static readonly Dictionary<string, int> StatusRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Obsolete"] = 4,
        ["NRND"] = 3,
        ["Active"] = 2,
        ["Unknown"] = 1
    };

    private readonly SourceDefinition[] _sources =
    [
        new(
            "DigiKey",
            1,
            "https://www.digikey.com/en/products/result?keywords={0}",
            ["digikey.com"],
            [
                new Regex(@"\bObsolete\b.{0,120}\bno\s+longer\s+manufactured\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bPart\s+Status\b.{0,40}\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bPart\s+Status\b.{0,40}\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bPart\s+Status\b.{0,80}\bNot\s+Recommended\s+for\s+New\s+Designs\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)
            ],
            [
                new Regex(@"""manufacturerName"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bManufacturer\b.{0,40}(?<value>[A-Z0-9][A-Za-z0-9\s\.\-&/,]{2,60})", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)
            ]),
        new(
            "Mouser",
            2,
            "https://www.mouser.com/c/?q={0}",
            ["mouser.com"],
            [
                new Regex(@"\bLifecycle:\b.{0,40}\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bLifecycle:\b.{0,60}\bNot\s+Recommended\s+for\s+New\s+Designs\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bLifecycle:\b.{0,40}\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bPossible\s+Replacement\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [
                new Regex(@"""manufacturer"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bMfr\.\s*:\s*(?<value>[A-Z0-9][A-Za-z0-9\s\.\-&/,]{2,60})", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ]),
        new(
            "Newark",
            3,
            "https://www.newark.com/search?st={0}",
            ["newark.com"],
            [
                new Regex(@"\bPart\s+Status\b.{0,40}\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled),
                new Regex(@"\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [new Regex(@"""brand"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]),
        new(
            "Arrow",
            4,
            "https://www.arrow.com/en/products/search?q={0}",
            ["arrow.com"],
            [
                new Regex(@"\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [new Regex(@"""brand"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]),
        new(
            "Future Electronics",
            5,
            "https://www.futureelectronics.com/search?text={0}",
            ["futureelectronics.com"],
            [
                new Regex(@"\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [new Regex(@"""brand"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]),
        new(
            "Analog Devices",
            6,
            "https://www.analog.com/en/search.html?q={0}",
            ["analog.com"],
            [
                new Regex(@"\bPRODUCTION\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bOBSOLETE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bNOT\s+RECOMMENDED\s+FOR\s+NEW\s+DESIGNS\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [
                new Regex(@"(?<value>Analog Devices)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"""brand"":\s*""(?<value>[^"]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            static (partNumber, manufacturer) =>
                PartLooksLike(partNumber, "LT", "LTC", "AD", "ADM", "ADP", "ADG", "ADA", "ADF", "ADUM") ||
                ManufacturerMatches(manufacturer, "Analog Devices")),
        new(
            "Texas Instruments",
            7,
            "https://www.ti.com/packaging/docs/partlookup.tsp?partnumber={0}",
            ["ti.com"],
            [
                new Regex(@"\bACTIVE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bLIFEBUY\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bNRND\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bOBSOLETE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [new Regex(@"(?<value>Texas Instruments)", RegexOptions.IgnoreCase | RegexOptions.Compiled)],
            static (partNumber, manufacturer) =>
                PartLooksLike(partNumber, "TL", "TPS", "LM", "SN", "OPA", "DAC", "ADC", "TCA", "TMP", "INA", "DRV", "BQ") ||
                ManufacturerMatches(manufacturer, "Texas Instruments")),
        new(
            "STMicroelectronics",
            8,
            "https://www.st.com/content/st_com/en/search.html?query={0}",
            ["st.com"],
            [
                new Regex(@"\bActive\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bObsolete\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new Regex(@"\bNRND\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            ],
            [new Regex(@"(?<value>STMicroelectronics)", RegexOptions.IgnoreCase | RegexOptions.Compiled)],
            static (partNumber, manufacturer) =>
                PartLooksLike(partNumber, "STM", "ST", "TSZ", "L99", "VIPER", "M24", "LSM") ||
                ManufacturerMatches(manufacturer, "STMicroelectronics"))
    ];

    public async Task<LifecycleResultRow> AnalyzePartAsync(string partNumber, CancellationToken cancellationToken)
    {
        var row = new LifecycleResultRow
        {
            PartNumber = partNumber,
            Summary = "Basic part information was not found on the inspected sources."
        };

        var manufacturerCandidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var evidenceHits = new List<EvidenceHit>();
        var summaryCandidates = new List<SummaryCandidate>();

        if (_digikeyApiClient.IsConfigured)
        {
            try
            {
                var apiResult = await _digikeyApiClient.TryAnalyzePartAsync(partNumber, cancellationToken);
                if (apiResult is not null)
                {
                    evidenceHits.Add(new EvidenceHit(apiResult.SourceName, 0, apiResult.Url, apiResult.Status, apiResult.Snippet));
                    manufacturerCandidates[apiResult.Manufacturer] =
                        manufacturerCandidates.TryGetValue(apiResult.Manufacturer, out var score) ? score + 8 : 8;
                    AddSummaryCandidate(summaryCandidates, apiResult.SourceName, 0, apiResult.ProductSummary);
                }
            }
            catch (Exception ex)
            {
                row.Notes.Add($"DigiKey API: request failed ({ex.Message})");
            }
        }

        if (_mouserApiClient.IsConfigured)
        {
            try
            {
                var apiResult = await _mouserApiClient.TryAnalyzePartAsync(partNumber, cancellationToken);
                if (apiResult is not null)
                {
                    evidenceHits.Add(new EvidenceHit(apiResult.SourceName, 1, apiResult.Url, apiResult.Status, apiResult.Snippet));
                    manufacturerCandidates[apiResult.Manufacturer] =
                        manufacturerCandidates.TryGetValue(apiResult.Manufacturer, out var score) ? score + 7 : 7;
                    AddSummaryCandidate(summaryCandidates, apiResult.SourceName, 1, apiResult.ProductSummary);
                }
            }
            catch (Exception ex)
            {
                row.Notes.Add($"Mouser API: request failed ({ex.Message})");
            }
        }

        foreach (var source in _sources.OrderBy(item => item.Priority))
        {
            var knownManufacturer = manufacturerCandidates
                .OrderByDescending(pair => pair.Value)
                .Select(pair => pair.Key)
                .FirstOrDefault();

            if (!source.ShouldQuery(partNumber, knownManufacturer))
            {
                continue;
            }

            var searchUrl = string.Format(source.SearchUrlTemplate, Uri.EscapeDataString(partNumber));
            string html;

            try
            {
                html = await GetStringSafeAsync(searchUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                AddRequestNote(row, source.Name, ex);
                continue;
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                row.Notes.Add($"{source.Name}: empty response");
                continue;
            }

            CollectManufacturerCandidates(source, html, manufacturerCandidates);
            TryAddEvidenceHit(evidenceHits, source, searchUrl, html);
            TryAddSummaryCandidate(summaryCandidates, source, html);

            foreach (var candidateUrl in ExtractCandidateUrls(html, searchUrl, source.AllowedHosts, partNumber).Take(4))
            {
                string candidateHtml;
                try
                {
                    candidateHtml = await GetStringSafeAsync(candidateUrl, cancellationToken);
                }
                catch (Exception ex)
                {
                    AddRequestNote(row, source.Name, ex);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(candidateHtml))
                {
                    continue;
                }

                CollectManufacturerCandidates(source, candidateHtml, manufacturerCandidates);
                TryAddEvidenceHit(evidenceHits, source, candidateUrl, candidateHtml);
                TryAddSummaryCandidate(summaryCandidates, source, candidateHtml);
            }
        }

        foreach (var hit in evidenceHits
                     .OrderBy(hit => hit.SourcePriority)
                     .ThenByDescending(hit => StatusRank.TryGetValue(hit.Status, out var rank) ? rank : 0)
                     .Take(8))
        {
            row.Evidence.Add(new EvidenceItem
            {
                SourceName = hit.SourceName,
                Url = hit.Url,
                Status = hit.Status,
                Snippet = hit.Snippet
            });
        }

        row.Manufacturer = ResolveManufacturer(manufacturerCandidates);
        row.OverallStatus = ResolveOverallStatus(evidenceHits);
        row.Summary = BuildSummary(summaryCandidates, row);
        return row;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        client.Timeout = TimeSpan.FromSeconds(45);
        return client;
    }

    private static async Task<string> GetStringSafeAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static void TryAddEvidenceHit(List<EvidenceHit> evidenceHits, SourceDefinition source, string url, string html)
    {
        var plainText = NormalizeWhitespace(RemoveHtml(html));
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return;
        }

        foreach (var pattern in source.StatusPatterns)
        {
            var match = pattern.Match(plainText);
            if (!match.Success)
            {
                continue;
            }

            var status = ClassifyStatus(source.Name, match.Value);
            var snippet = ExtractSnippet(plainText, match.Index);

            if (evidenceHits.Any(item =>
                    item.SourceName.Equals(source.Name, StringComparison.OrdinalIgnoreCase) &&
                    item.Url.Equals(url, StringComparison.OrdinalIgnoreCase) &&
                    item.Status.Equals(status, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            evidenceHits.Add(new EvidenceHit(source.Name, source.Priority, url, status, snippet));
            return;
        }
    }

    private static string ClassifyStatus(string sourceName, string snippet)
    {
        var normalized = NormalizeWhitespace(snippet).ToLowerInvariant();

        if (normalized.Contains("obsolete") ||
            normalized.Contains("no longer manufactured") ||
            normalized.Contains("end of life") ||
            normalized.Contains("possible replacement"))
        {
            return "Obsolete";
        }

        if (normalized.Contains("nrnd") ||
            normalized.Contains("not recommended for new designs") ||
            normalized.Contains("lifebuy"))
        {
            return "NRND";
        }

        if (sourceName.Equals("Analog Devices", StringComparison.OrdinalIgnoreCase) &&
            normalized.Contains("production"))
        {
            return "Active";
        }

        if (normalized.Contains("active"))
        {
            return "Active";
        }

        return "Unknown";
    }

    private static IEnumerable<string> ExtractCandidateUrls(string html, string baseUrl, IReadOnlyCollection<string> allowedHosts, string partNumber)
    {
        var regex = new Regex("""href\s*=\s*[""'](?<url>[^""'#>]+)[""']""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var partRegex = new Regex(Regex.Escape(partNumber), RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in regex.Matches(html))
        {
            var rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (rawUrl.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Uri.TryCreate(new Uri(baseUrl), rawUrl, out var uri))
            {
                continue;
            }

            if (!allowedHosts.Any(host => uri.Host.Contains(host, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var absoluteUrl = uri.ToString();
            if (!partRegex.IsMatch(absoluteUrl))
            {
                continue;
            }

            if (seen.Add(absoluteUrl))
            {
                yield return absoluteUrl;
            }
        }
    }

    private static void CollectManufacturerCandidates(SourceDefinition source, string html, IDictionary<string, int> candidates)
    {
        foreach (var regex in source.ManufacturerPatterns)
        {
            var match = regex.Match(html);
            if (!match.Success)
            {
                continue;
            }

            var valueGroup = match.Groups["value"];
            var value = valueGroup.Success ? valueGroup.Value : match.Value;
            value = NormalizeManufacturer(value);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var weight = source.Priority <= 2 ? 4 : source.Priority <= 5 ? 2 : 1;
            candidates[value] = candidates.TryGetValue(value, out var score) ? score + weight : weight;
        }
    }

    private static void TryAddSummaryCandidate(List<SummaryCandidate> summaryCandidates, SourceDefinition source, string html)
    {
        var plainText = NormalizeWhitespace(RemoveHtml(html));
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return;
        }

        string? summary = null;
        var summaryPatterns = new[]
        {
            new Regex(@"\bDetailed\s+Description\b[:\s]+(?<value>.{20,220}?)\b(?:Datasheet|Manufacturer|Package|Quantity|Part\s+Status)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline),
            new Regex(@"\bDescription\b[:\s]+(?<value>.{20,220}?)\b(?:Datasheet|Manufacturer|Package|Quantity|Lifecycle|Availability)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline),
            new Regex(@"\bProduct\s+Description\b[:\s]+(?<value>.{20,220}?)\b(?:Datasheet|Manufacturer|Package|Quantity|Lifecycle|Availability)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)
        };

        foreach (var pattern in summaryPatterns)
        {
            var match = pattern.Match(plainText);
            if (!match.Success)
            {
                continue;
            }

            summary = NormalizeSummary(match.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        AddSummaryCandidate(summaryCandidates, source.Name, source.Priority, summary);
    }

    private static string ResolveManufacturer(IDictionary<string, int> candidates)
    {
        if (candidates.Count == 0)
        {
            return "Unknown";
        }

        var selected = candidates
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .First();

        return selected.Key;
    }

    private static string ResolveOverallStatus(IEnumerable<EvidenceHit> evidenceHits)
    {
        var prioritizedHit = evidenceHits
            .Where(hit => hit.SourcePriority <= 2)
            .OrderByDescending(hit => StatusRank.TryGetValue(hit.Status, out var rank) ? rank : 0)
            .ThenBy(hit => hit.SourcePriority)
            .FirstOrDefault();

        if (prioritizedHit is not null)
        {
            return prioritizedHit.Status;
        }

        var fallbackHit = evidenceHits
            .OrderByDescending(hit => StatusRank.TryGetValue(hit.Status, out var rank) ? rank : 0)
            .ThenBy(hit => hit.SourcePriority)
            .FirstOrDefault();

        return fallbackHit?.Status ?? "Unknown";
    }

    private static string BuildSummary(IEnumerable<SummaryCandidate> summaryCandidates, LifecycleResultRow row)
    {
        var summary = summaryCandidates
            .OrderBy(candidate => candidate.SourcePriority)
            .ThenByDescending(candidate => candidate.Text.Length)
            .Select(candidate => candidate.Text)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        if (row.Evidence.Count == 0)
        {
            return "Basic part information was not found on the inspected sources.";
        }

        var fallback = row.Evidence
            .Select(item => NormalizeSummary(item.Snippet))
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        return string.IsNullOrWhiteSpace(fallback)
            ? "Basic part information was not found on the inspected sources."
            : fallback;
    }

    private static string RemoveHtml(string html)
    {
        var withoutScript = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScript, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string NormalizeWhitespace(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();

    private static string ExtractSnippet(string text, int startIndex)
    {
        var snippetStart = Math.Max(0, startIndex - 60);
        var snippetLength = Math.Min(240, text.Length - snippetStart);
        return NormalizeWhitespace(text.Substring(snippetStart, snippetLength));
    }

    private static string NormalizeManufacturer(string value)
    {
        value = RemoveHtml(value);
        value = NormalizeWhitespace(value);
        value = value
            .Replace("Manufacturer", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Mfr.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim(' ', ':', '-', '|');

        if (value.Contains("MEAN WELL USA", StringComparison.OrdinalIgnoreCase))
        {
            return "MEAN WELL USA Inc.";
        }

        if (value.Length > 80)
        {
            value = value[..80].Trim();
        }

        return value;
    }

    private static string NormalizeSummary(string value)
    {
        value = NormalizeWhitespace(value);
        value = Regex.Replace(value, @"^(DigiKey API status|Mouser API lifecycle)\s*:\s*[^\.]+\.\s*", string.Empty, RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b(Suggested replacement|Series|Category|Packaging|Package)\s*:\s*", "$1: ", RegexOptions.IgnoreCase);

        if (value.Length > 220)
        {
            value = value[..220].Trim();
        }

        return value.Trim(' ', '.', ';');
    }

    private static void AddSummaryCandidate(List<SummaryCandidate> summaryCandidates, string sourceName, int sourcePriority, string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        var normalized = NormalizeSummary(summary);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (summaryCandidates.Any(candidate => candidate.Text.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        summaryCandidates.Add(new SummaryCandidate(sourceName, sourcePriority, normalized));
    }

    private static bool PartLooksLike(string partNumber, params string[] prefixes) =>
        prefixes.Any(prefix => partNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool ManufacturerMatches(string? manufacturer, string expected) =>
        !string.IsNullOrWhiteSpace(manufacturer) &&
        manufacturer.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private static void AddRequestNote(LifecycleResultRow row, string sourceName, Exception ex)
    {
        var message = ex switch
        {
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.Forbidden =>
                $"{sourceName}: blocked by website (403)",
            HttpRequestException httpEx when httpEx.StatusCode == HttpStatusCode.NotFound =>
                $"{sourceName}: search page not available (404)",
            TaskCanceledException =>
                $"{sourceName}: request timed out",
            HttpRequestException =>
                $"{sourceName}: network or site access issue",
            _ => $"{sourceName}: request failed"
        };

        if (!row.Notes.Contains(message))
        {
            row.Notes.Add(message);
        }
    }

    private sealed record SourceDefinition(
        string Name,
        int Priority,
        string SearchUrlTemplate,
        IReadOnlyCollection<string> AllowedHosts,
        IReadOnlyCollection<Regex> StatusPatterns,
        IReadOnlyCollection<Regex> ManufacturerPatterns,
        Func<string, string?, bool>? QueryWhen = null)
    {
        public bool ShouldQuery(string partNumber, string? manufacturer) => QueryWhen?.Invoke(partNumber, manufacturer) ?? true;
    }

    private sealed record EvidenceHit(
        string SourceName,
        int SourcePriority,
        string Url,
        string Status,
        string Snippet);

    private sealed record SummaryCandidate(
        string SourceName,
        int SourcePriority,
        string Text);
}
