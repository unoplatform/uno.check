using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DotNetCheck.Models;

namespace DotNetCheck.Checkups
{
    public class VSRestartCheckup : Checkup
    {
        public override bool IsPlatformSupported(Platform platform)
            => platform == Platform.Windows;

        public override string Id => "vsrestart";

        public override string Title => "Visual Studio Restart Check";

        public override bool ShouldExamine(SharedState history)
        {
            return history.TryGetState<bool>(StateKey.EntryPoint, "DotNetWorkloadsInstalled", out var workloadsInstalled) && workloadsInstalled
                || history.TryGetState<bool>(StateKey.EntryPoint, "DotNetSdkInstalled", out var sdkInstalled) && sdkInstalled;
        }

        public override Task<DiagnosticResult> Examine(SharedState history)
        {
            try
            {
                var vsProcesses = Process.GetProcessesByName("devenv");
                
                if (vsProcesses.Length > 0)
                {
                    ReportStatus("Visual Studio is currently running", Status.Warning);
                    return Task.FromResult(new DiagnosticResult(
                        Status.Warning,
                        this,
                        new Suggestion(
                            "Restart Visual Studio",
                            "Some checks requires that Visual Studio be restarted to take effect")));
                }
            }
            catch (Exception ex)
            {
                Util.Exception(ex);
            }

            return Task.FromResult(DiagnosticResult.Ok(this));
        }
    }
} 