#nullable enable

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using System.Linq;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups;

internal class DotNetNewUnoTemplatesCheckup : Checkup
{
    private const string TemplatesDisplayName = "Uno Platform";
    private const string PackageName = "Uno.Templates";

    private Regex _dotNetNewOutputRegex = new(
        pattern: @"Uno\.Templates\s*$\s*Version: (.*)",
        options: RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public override string Id => "dotnetnewunotemplates";

    public override string Title => "dotnet new Uno Project Templates";

    public override async Task<DiagnosticResult> Examine(SharedState history)
    {
        var dotnetOutput = GetDotNetNewInstalledList();
        var version = GetInstalledVersion(dotnetOutput, _dotNetNewOutputRegex);
        if (version is null)
        {
            return new(
                Status.Error,
                this,
                new Suggestion(
                    $"The {TemplatesDisplayName} dotnet new templates are not installed.",
                    new DotNetNewTemplatesInstallSolution(PackageName)));
        }

        var latestVersion = await NuGetHelper.GetLatestPackageVersionAsync(
            PackageName,
            ToolInfo.CurrentVersion.IsPrerelease);

        if (latestVersion > version)
        {
            return new(
                Status.Error,
                this,
                new Suggestion(
                    $"The {TemplatesDisplayName} dotnet new templates are not up to date " +
                    $"(installed version {version}, latest available version {latestVersion}.",
                    new DotNetNewTemplatesInstallSolution(PackageName, latestVersion)));
        }

        return DiagnosticResult.Ok(this);
    }

    private string GetDotNetNewInstalledList()
    {
        // Running 'dotnet new uninstall' without any package ID will list all installed
        // dotnet new templates along with their versions.
        var processInfo = new ProcessStartInfo("dotnet", "new uninstall");
        processInfo.RedirectStandardOutput = true;
        processInfo.UseShellExecute = false;

        var process = Process.Start(processInfo);
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    private NuGetVersion? GetInstalledVersion(string dotnetNewOutput, Regex regex)
    {
        var match = regex.Match(dotnetNewOutput);
        if (match.Success)
        {
            var version = match.Groups.Values.Last();
            if (NuGetVersion.TryParse(version.Value, out var parsedVersion))
            {
                return parsedVersion;
            }
        }

        return null;
    }
}
