using DotNetCheck.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNetCheck.Checkups;

namespace DotNetCheck.Solutions;

public sealed class VsixInstallSolution : Solution
{
    public override async Task Implement(SharedState state, CancellationToken ct)
    {
        using var marketplace = new UnoPlatformMarketplaceService();
        
        var vsixPath = Path.Combine(Path.GetTempPath(), $"uno-vsix-{Guid.NewGuid()}.vsix");
        await marketplace.DownloadExtensionPackageAsync(vsixPath, ct);
        
        var vsPath = GetVsixInstallerPath();
        ReportStatus($"Installing {Path.GetFileName(vsixPath)}...");

        var pr = ShellProcessRunner.Run(vsPath, $"/q \"{vsixPath}\"");
        if (pr.ExitCode != 0)
            throw new InvalidOperationException($"VSIXInstaller failed (code {pr.ExitCode}).");
    }

    private static string GetVsixInstallerPath()
    {
        var vsInfos = VisualStudioWindowsCheckup.GetWindowsInfo();
        
        var vs2022 = vsInfos
            .FirstOrDefault(v => v.Version.Major == 17);

        var vsixPath = Path.Combine(vs2022.Path, "Common7", "IDE", "VSIXInstaller.exe");
        
        if (!File.Exists(vsixPath))
            throw new FileNotFoundException("VSIXInstaller.exe not found.");

        return vsixPath;
    }
}
