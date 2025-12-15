using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
using NuGet.Versioning;

namespace DotNetCheck.Checkups;

public sealed class VSPluginInstallationCheckup : Checkup
{
    public override string Id => "unovsextension";
    public override string Title => "Uno Platform Visual Studio Extension";

    public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> _) =>
        [new("vswindows")];

    public override bool IsPlatformSupported(Platform p) => p == Platform.Windows;
    public override async Task<DiagnosticResult> Examine(SharedState state)
    {
        var windowsInfo = VisualStudioWindowsCheckup.GetWindowsInfo();
        if (windowsInfo is not { Count: > 0 } ||
            !VisualStudioInstanceSelector.TryGetLatestSupportedInstance(windowsInfo, out var vsInfo))
        {
            return DiagnosticResult.Ok(this);
        }

        var extensionsPath = GetExtensionsPath(vsInfo);
        if (!Directory.Exists(extensionsPath))
        {
            return SuggestVsixInstall();
        }

        var installedVersion = FindInstalledUnoVsixVersion(extensionsPath);
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return SuggestVsixInstall();
        }

        var marketplaceVersion = await TryGetMarketplaceVersionAsync();
        if (!string.IsNullOrWhiteSpace(marketplaceVersion) &&
            NuGetVersion.TryParse(installedVersion, out var localVer) &&
            NuGetVersion.TryParse(marketplaceVersion, out var marketVer) &&
            localVer < marketVer)
        {
            return new DiagnosticResult(
                Status.Error,
                this,
                new Suggestion($"Installed version {localVer} is out-of-date (latest is {marketVer}).",
                    new VsixInstallSolution()));
        }

        return DiagnosticResult.Ok(this);
    }

    private static string GetExtensionsPath(VisualStudioInfo vsInfo) => 
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "VisualStudio", 
            $"{vsInfo.Version.Major}.0_{vsInfo.InstanceId}", 
            "Extensions");

    private DiagnosticResult SuggestVsixInstall()
    {
        return new DiagnosticResult(
            Status.Warning,
            this,
            new Suggestion("Install Uno Platform VS extension for the best experience.",
                new VsixInstallSolution()));
    }
    
    private static string FindInstalledUnoVsixVersion(string extensionsDir)
    {
        foreach (var manifestPath in Directory.EnumerateFiles(extensionsDir, "*.vsixmanifest", SearchOption.AllDirectories))
        {
            var manifestXml = XDocument.Load(manifestPath);

            var identity = manifestXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            if (identity == null)
            {
                continue;
            }

            if (!string.Equals(identity.Attribute("Publisher")?.Value, "Uno Platform", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return identity.Attribute("Version")?.Value;
        }

        return null;
    }

    private async Task<string?> TryGetMarketplaceVersionAsync()
    {
        try
        {
            using var marketplace = new UnoPlatformMarketplaceService();
            var marketplaceDetails = await marketplace.GetExtensionDetailsAsync();
            return marketplaceDetails.Version;
        }
        catch (Exception ex)
        {
            Util.Log($"Marketplace query failed: {ex.Message}");
            Util.Exception(ex);
            ReportStatus("Could not query Visual Studio Marketplace for latest Uno extension version (skipping update check).", null);

            return null;
        }
    }
}
