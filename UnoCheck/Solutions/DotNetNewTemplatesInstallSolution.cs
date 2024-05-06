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
        var dotnetCommand = new DotNetSdk(sharedState).DotNetExecutable;

        var version = _requestedVersion ??
            await NuGetHelper.GetLatestPackageVersionAsync(UnoTemplatesPackageName, ToolInfo.CurrentVersion.IsPrerelease);

        if (_uninstallLegacy)
        {
            var uninstallCli = new ShellProcessRunner(new ShellProcessRunnerOptions(dotnetCommand, $"new uninstall {UnoLegacyTemplatesPackageName}"));
            uninstallCli.WaitForExit();
        }

        if (_uninstallExisting)
        {
            var uninstallCli = new ShellProcessRunner(new ShellProcessRunnerOptions(dotnetCommand, $"new uninstall {UnoTemplatesPackageName}"));
            uninstallCli.WaitForExit();
        }

        var cli = new ShellProcessRunner(new ShellProcessRunnerOptions(dotnetCommand, $"new install {UnoTemplatesPackageName}::{version}") { Verbose = Util.Verbose });
        cli.WaitForExit();
    }
}
