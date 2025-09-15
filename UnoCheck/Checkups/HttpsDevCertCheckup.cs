using System.IO;
using System.Threading.Tasks;
using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups
{
    public class HttpsDevCertCheckup : Checkup
    {
        public override string Id => "https-dev-cert";
        public override string Title => "HTTPS Developer Certificate Trust";
        public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;
        
        // Only examine when WebAssembly is one of the targets
        public override bool ShouldExamine(SharedState history) => 
            history.TryGetState<TargetPlatform>(StateKey.EntryPoint, StateKey.TargetPlatforms, out var platforms)
            && platforms.HasFlag(TargetPlatform.WebAssembly);

        public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) =>
            TargetPlatform.WebAssembly;

        public override async Task<DiagnosticResult> Examine(SharedState history)
        {
            if (Util.CI || Util.NonInteractive)
            {
                ReportStatus("Skipping in CI / non-interactive mode", Status.Ok);
                return DiagnosticResult.Ok(this);
            }
            
            var check = await Util.ShellCommand(
                "dotnet",
                Directory.GetCurrentDirectory(),
                Util.Verbose,
                ["dev-certs", "https", "-c", "-t"]);

            if (check.ExitCode == 0)
            {
                ReportStatus("Developer HTTPS certificates are trusted", Status.Ok);
                return DiagnosticResult.Ok(this);
            }

            ReportStatus("Developer HTTPS certificates are not trusted", Status.Error);

            var suggestion = new Suggestion(
                "Trust HTTPS developer certificates",
                "This command will trust your local dev certs so that HTTPS works correctly in WebAssembly apps.",
                new ActionSolution(async (_, _) =>
                {
                    await Util.ShellCommand(
                        "dotnet",
                        Directory.GetCurrentDirectory(),
                        Util.Verbose,
                        ["dev-certs", "https", "--trust"]);
                })
            );

            return new DiagnosticResult(Status.Error, this, suggestion);
        }
    }
}
