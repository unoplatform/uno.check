#nullable enable

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using NuGet.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions;

internal class DotNetNewTemplatesInstallSolution : Solution
{
    private const string UnoLegacyTemplatesPackageName = "Uno.ProjectTemplates.Dotnet";
    private const string UnoTemplatesPackageName = "Uno.Templates";

    private readonly bool _uninstallLegacy;
    private readonly bool _uninstallExisting;
    private readonly NuGetVersion? _requestedVersion;

    /// <summary>
    /// <c>dotnet new install/uninstall</c> writes the per-user template engine store
    /// (~/.templateengine), never a machine location.
    /// </summary>
    public override bool RequiresElevation => false;

    public DotNetNewTemplatesInstallSolution(
        bool uninstallLegacy, 
        bool uninstallExisting, 
        NuGetVersion? requestedVersion = null)
    {
        _uninstallLegacy = uninstallLegacy;
        _uninstallExisting = uninstallExisting;
        _requestedVersion = requestedVersion;
    }

    public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
    {
        var version = _requestedVersion ??
            await NuGetHelper.GetLatestPackageVersionAsync(UnoTemplatesPackageName, ToolInfo.CurrentVersion.IsPrerelease);

        // The uninstalls are best-effort housekeeping: 'dotnet new uninstall' exits non-zero
        // when the package was never installed, which is the expected state here and must not
        // fail the fix. Only the install below determines whether the fix worked.
        if (_uninstallLegacy)
        {
            var uninstallCli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new uninstall {UnoLegacyTemplatesPackageName}"));
            uninstallCli.WaitForExit();
        }

        if (_uninstallExisting)
        {
            var uninstallCli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new uninstall {UnoTemplatesPackageName}"));
            uninstallCli.WaitForExit();
        }

        var cli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new install {UnoTemplatesPackageName}::{version}") { Verbose = Util.Verbose });

        // Discarding this exit code reported an applied fix for an install that never ran.
        Util.ThrowIfFailed(cli.WaitForExit(), $"'dotnet new install {UnoTemplatesPackageName}::{version}'");
    }
}
