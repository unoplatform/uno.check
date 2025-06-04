using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Versioning;
using Spectre.Console;

namespace DotNetCheck;

internal static class ToolUpdater
{
    private enum UserAction
    {
        Continue,
        Update,
        Stop
    }

    public static async Task<bool> CheckAndPromptForUpdateAsync(CheckSettings settings)
    {
        if ((!string.IsNullOrEmpty(settings.Manifest) && !settings.CI) || Debugger.IsAttached)
        {
            return false;
        }
        
        var currentVersion = ToolInfo.CurrentVersion;
        NuGetVersion latestVersion = null;

        try
        {
            latestVersion = await ToolInfo.GetLatestVersion(currentVersion.IsPrerelease);
            if (string.IsNullOrEmpty(latestVersion?.ToString()))
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            if (Util.Verbose)
            {
                AnsiConsole.WriteException(ex);   
            }
            
            AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Could not check for latest version on NuGet.org. The currently installed version may be out of date.[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule());
        }

        if (currentVersion < latestVersion)
        {
            AnsiConsole.MarkupLine($"[bold yellow]{Icon.Warning} Your uno-check version is not up to date. The latest version is {latestVersion}. You can update with:[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"dotnet tool update --global {ToolInfo.ToolPackageId} --version {latestVersion}");
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule());
            
            if (!settings.CI)
            {
                var action = AnsiConsole.Prompt(
                    new SelectionPrompt<UserAction>()
                        .Title("What would you like to do?")
                        .AddChoices(UserAction.Continue, UserAction.Update, UserAction.Stop)
                        .UseConverter(action => action switch
                        {
                            UserAction.Continue => "[green]Continue[/]",
                            UserAction.Update => "[yellow]Update[/]",
                            UserAction.Stop => "[red]Stop[/]",
                            _ => action.ToString()
                        })
                        .PageSize(3)
                );

                if (action == UserAction.Update)
                {
                    RelaunchWithUpdate(latestVersion!.ToString());
                }

                return action == UserAction.Stop;
            }
        }

        return false;
    }

    private static void RelaunchWithUpdate(string version)
    {
        var argsLine = string.Join(" ", 
            Environment.GetCommandLineArgs().Skip(1)
                   .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)
        );

        PerformUpdateAndRelaunch(version, argsLine);
    }

    private static void PerformUpdateAndRelaunch(string version, string argsForRelaunch)
    {
        if (Util.IsWindows)
        {
            var cmdText =
                $"timeout /T 2 /NOBREAK && " +
                $"dotnet tool update --global {ToolInfo.ToolPackageId} --version {version} " +
                $"&& start \"\" {ToolInfo.ToolCommand} {argsForRelaunch}";

            Process.Start(new ProcessStartInfo("cmd.exe", $"/C {cmdText}")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        else
        {
            var shCommand =
                $"sleep 1 && " +
                $"dotnet tool update --global {ToolInfo.ToolPackageId} --version {version} " +
                $"&& {ToolInfo.ToolCommand} {argsForRelaunch}".Trim();

            var full = $"\"{shCommand} 2>&1\"";

            Process.Start(new ProcessStartInfo("/bin/sh", $"-c {full}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        
        Environment.Exit(0);
    }
}
