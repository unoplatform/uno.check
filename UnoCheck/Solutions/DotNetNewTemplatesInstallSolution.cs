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
    private readonly NuGetVersion? _requestedVersion;

    public DotNetNewTemplatesInstallSolution(string packageName, NuGetVersion? requestedVersion = null)
    {
        _packageName = packageName;
        _requestedVersion = requestedVersion;
    }

    public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
    {
        var version = _requestedVersion ??
            await NuGetHelper.GetLatestPackageVersionAsync(_packageName, ToolInfo.CurrentVersion.IsPrerelease);

        var cli = new ShellProcessRunner(new ShellProcessRunnerOptions("dotnet", $"new install {_packageName}::{version}") { Verbose = Util.Verbose });
        cli.WaitForExit();
    }
}
