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
        if (windowsInfo is { Count: > 0 })
        {
            var vsInfo = windowsInfo.FirstOrDefault(x => x.Version.Major == 17);
            if (!string.IsNullOrEmpty(vsInfo.Path))
            {
                var extensionsPath = GetExtensionsPath(vsInfo);
                if (Directory.Exists(extensionsPath))
                {
                    using var marketplace = new UnoPlatformMarketplaceService();
                    var marketplaceDetails = await marketplace.GetExtensionDetailsAsync();
                    
                    var installedVersion = FindInstalledUnoVsixVersion(extensionsPath);
                    if (string.IsNullOrEmpty(installedVersion))
                    {
                        return SuggestVsixInstall();
                    }
                    
                    if (NuGetVersion.TryParse(installedVersion, out var localVer) &&
                        NuGetVersion.TryParse(marketplaceDetails.Version, out var marketVer) &&
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

                return SuggestVsixInstall();
            }
        }
        return DiagnosticResult.Ok(this);
    }

    private static string GetExtensionsPath(VisualStudioInfo vsInfo) => 
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "VisualStudio", 
            $"{vsInfo.Version.Version.Major}.0_{vsInfo.InstanceId}", 
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
}