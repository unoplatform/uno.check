using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using DotNetCheck.Models;

namespace DotNetCheck.Checkups;

public sealed class VSPluginInstallationCheckup : Checkup
{
    private const string MarketplaceUrl =
        "https://marketplace.visualstudio.com/_apis/public/gallery/extensionquery?api-version=3.0-preview.1";
    
    private static readonly Regex VersionRegex = new(@"(?<=\x22Version\x22\s*:\s*\x22)\d+(?:\.\d+)*(?=\x22)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    public override string Id => "unovsextension";
    public override string Title => "Uno Platform Visual Studio Extension";

    public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> _) =>
        [new("vswindows")];

    public override bool IsPlatformSupported(Platform p) => p == Platform.Windows;

    public override async Task<DiagnosticResult> Examine(SharedState state)
    {
        var windowsInfo = await VisualStudioWindowsCheckup.GetWindowsInfo();

        var vsInfo = windowsInfo.First();
        var extensionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "VisualStudio", $"{vsInfo.Version.Version.Major}.0_{vsInfo.InstanceId}", "Extensions");
        if (!Directory.Exists(extensionsPath))
            return null;
        var installedVersion = FindInstalledUnoVsixVersion(extensionsPath);
        var latestVersion = await GetLatestMarketplaceVersion();
        return null;
    }

    private static string FindInstalledUnoVsixVersion(string extensionsDir)
    {
        foreach (var vsixDir in Directory.GetDirectories(extensionsDir, "*", SearchOption.AllDirectories))
        {
            var manifestPath = Directory.GetFiles(vsixDir, "*.vsixmanifest").FirstOrDefault();
            if (manifestPath == null)
                continue;

            var manifestXml = XDocument.Load(manifestPath);
            var metadata = manifestXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Metadata");

            var identity = metadata?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            if (identity == null || identity.Attribute("Publisher")?.Value != "Uno Platform")
                continue;
            
            var versionAttr = identity.Attribute("Version");
            
            return versionAttr?.Value;
        }

        return null;
    }
    
    private static async Task<string> GetLatestMarketplaceVersion()
    {
        var payload = new ExtensionQueryRequest(
            filters:
            [
                new Filter(
                    criteria:
                    [
                        new Criterion(filterType: 7, value: "unoplatform.uno-platform-addin-2022")
                    ])
            ], assetTypes: ["Microsoft.VisualStudio.Services.VSIXPackage"], flags: 914);
        
        var jsonBody = JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        using var client = new HttpClient();
        
        var msg = new HttpRequestMessage(HttpMethod.Post, MarketplaceUrl)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
            Headers =
            {
                Accept =
                {
                    new MediaTypeWithQualityHeaderValue("application/json")
                }
            }
        };
        
        using var res = await client.SendAsync(msg);
        res.EnsureSuccessStatusCode();
        
        var match = VersionRegex.Match(await res.Content.ReadAsStringAsync());
        if (!match.Success)
            throw new InvalidOperationException("Marketplace response missing version.");

        return match.Value;
    }
    
    private sealed record ExtensionQueryRequest(Filter[] filters, string[] assetTypes, int flags);
    private sealed record Filter(Criterion[] criteria);
    private sealed record Criterion(int filterType, string value);
}