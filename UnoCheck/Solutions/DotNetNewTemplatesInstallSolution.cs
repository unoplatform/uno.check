#nullable enable

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using NuGet.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Xamarin.AndroidTools;

namespace DotNetCheck.Solutions;

internal class DotNetNewTemplatesInstallSolution : Solution
{
    private readonly string _packageName;
    private readonly bool _uninstallExisting;
    private readonly NuGetVersion? _requestedVersion;

    public DotNetNewTemplatesInstallSolution(string packageName, bool uninstallExisting, NuGetVersion? requestedVersion = null)
    {
        _packageName = packageName;
        _uninstallExisting = uninstallExisting;
        _requestedVersion = requestedVersion;
    }

    public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
    {
        var version = _requestedVersion ??
            await NuGetHelper.GetLatestPackageVersionAsync(_packageName, ToolInfo.CurrentVersion.IsPrerelease);

        if (_uninstallExisting)
        {
            var uninstallCli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new uninstall {_packageName}"));
            uninstallCli.WaitForExit();
        }

        var cli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new install {_packageName}::{version}") { Verbose = Util.Verbose });
        cli.WaitForExit();
    }
}
