using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNetCheck.Checkups;
using DotNetCheck.Models;

namespace DotNetCheck.Solutions;

public sealed class VsixInstallSolution : Solution
{
    public override async Task Implement(SharedState state, CancellationToken ct)
    {
        if (IsVisualStudioRunning())
        {
            ReportStatus("Visual Studio is running. Close it and run uno-check again to install the extension.");
            return;
        }

        using var marketplace = new UnoPlatformMarketplaceService();

        var vsixPath = Path.Combine(Path.GetTempPath(), $"uno-vsix-{Guid.NewGuid()}.vsix");
        await marketplace.DownloadExtensionPackageAsync(vsixPath, ct);

        var vsPath = GetVsixInstallerPath();
        ReportStatus($"Installing {Path.GetFileName(vsixPath)}...");

        var pr = ShellProcessRunner.Run(vsPath, $"/q \"{vsixPath}\"");
        if (pr.ExitCode != 0)
            throw new InvalidOperationException($"VSIXInstaller failed (code {pr.ExitCode}).");
    }

    private string GetVsixInstallerPath()
    {
        var vsInfos = VisualStudioWindowsCheckup.GetWindowsInfo();

        if (!VisualStudioInstanceSelector.TryGetLatestSupportedInstance(vsInfos, out var vsInfo))
        {
			ReportStatus("No supported Visual Studio installation found (requires VS 2022+).");
			return null;
        }

        var vsixPath = Path.Combine(vsInfo.Path, "Common7", "IDE", "VSIXInstaller.exe");

        if (!File.Exists(vsixPath))
            throw new FileNotFoundException("VSIXInstaller.exe not found.");

        return vsixPath;
    }

    private static bool IsVisualStudioRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("devenv");
        }
        catch (Exception ex)
        {
            Util.Exception(ex);
            return true;
        }

        try
        {
            return processes is { Length: > 0 };
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
