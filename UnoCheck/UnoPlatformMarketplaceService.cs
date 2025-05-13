using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotNetCheck.Models;

namespace DotNetCheck;

public sealed class UnoPlatformMarketplaceService : IDisposable
{
    private const string MarketplaceApiUrl = 
        "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery?api-version=3.0-preview.1";
    private const string Publisher = "unoplatform";
    private const string ExtensionId = "uno-platform-addin-2022";
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient = CreateHttpClient();

    public async Task<ExtensionDetails> GetExtensionDetailsAsync(CancellationToken ct = default)
    {
        var payload = CreateQueryPayload();
        var response = await PostMarketplaceRequest(payload, ct);
        return ParseResponse(response);
    }

    public async Task<string> DownloadExtensionPackageAsync(string savePath, CancellationToken ct = default)
    {
        var details = await GetExtensionDetailsAsync(ct);
        return await DownloadFileAsync(details.DownloadUrl, savePath, ct);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static ExtensionQueryRequest CreateQueryPayload() =>
        new([new Filter([new Criterion(7, $"{Publisher}.{ExtensionId}")])],
            ["Microsoft.VisualStudio.Services.VSIXPackage"], 
            914);

    private async Task<string> PostMarketplaceRequest(ExtensionQueryRequest payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient.PostAsync(MarketplaceApiUrl, content, ct);
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadAsStringAsync();
    }

    private static ExtensionDetails ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var versionElement = doc.RootElement
            .GetProperty("results")[0]
            .GetProperty("extensions")[0]
            .GetProperty("versions")[0];

        return new ExtensionDetails(
            Version: versionElement.GetProperty("version").GetString() ?? string.Empty,
            DownloadUrl: GetDownloadUrl(versionElement)
        );
    }

    private static string GetDownloadUrl(JsonElement versionElement)
    {
        var baseUrl = versionElement.TryGetProperty("fallbackAssetUri", out var fUri)
            ? fUri.GetString()
            : versionElement.GetProperty("assetUri").GetString();

        var fileName = GetFileName(versionElement);
        return $"{baseUrl?.TrimEnd('/')}/{fileName}";
    }

    private static string GetFileName(JsonElement versionElement)
    {
        const string defaultFileName = "Microsoft.VisualStudio.Services.VSIXPackage";
        
        if (!versionElement.TryGetProperty("properties", out var props))
            return defaultFileName;

        foreach (var p in props.EnumerateArray())
        {
            if (p.GetProperty("key").GetString() == 
                "Microsoft.VisualStudio.Services.Payload.FileName")
            {
                return p.GetProperty("value").GetString() ?? defaultFileName;
            }
        }
        
        return defaultFileName;
    }

    private async Task<string> DownloadFileAsync(string url, string savePath, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var downloadStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(savePath);
        
        await downloadStream.CopyToAsync(fileStream, ct);
        return savePath;
    }

    public void Dispose() => _httpClient.Dispose();
}

public record ExtensionDetails(string Version, string DownloadUrl);
