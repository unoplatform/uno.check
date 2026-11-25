#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using NuGet.Configuration;

namespace DotNetCheck.DotNet;

/// <summary>
/// uno-check runs some .NET commands from a scratch directory, where the user's nuget.config files are not
/// found. This writes a nuget.config there that carries the sources resolved for the user's directory.
/// </summary>
/// <remarks>
/// Credentials stored as settings are deliberately not copied: values can be expanded from environment variables.
/// Credentials stored in user-level settings still apply, because NuGet matches them by source name. A source URL can
/// itself carry a secret, so the directory and file are made private to the current user on Unix.
/// </remarks>
internal static class ScratchNuGetConfig
{
    public const string FileName = "nuget.config";

    public static string Write(
        string directory,
        IEnumerable<PackageSource> sources,
        string? globalPackagesFolder = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? connectionAttributes = null)
    {
        var packageSources = new XElement("packageSources", new XElement("clear"));
        foreach (var source in sources)
        {
            var add = new XElement("add", new XAttribute("key", source.Name), new XAttribute("value", source.Source));
            if (source.ProtocolVersion != PackageSource.DefaultProtocolVersion)
                add.Add(new XAttribute("protocolVersion", source.ProtocolVersion));

            if (connectionAttributes is not null && connectionAttributes.TryGetValue(source.Name, out var attributes))
            {
                foreach (var attribute in attributes)
                    add.Add(new XAttribute(attribute.Key, attribute.Value));
            }

            packageSources.Add(add);
        }

        var configuration = new XElement("configuration", packageSources, new XElement("disabledPackageSources", new XElement("clear")));
        if (!string.IsNullOrEmpty(globalPackagesFolder))
        {
            configuration.Add(new XElement("config",
                new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", globalPackagesFolder))));
        }

        var path = Path.Combine(directory, FileName);
        RestrictToCurrentUser(directory, "700");
        new XDocument(new XDeclaration("1.0", "utf-8", null), configuration).Save(path);
        RestrictToCurrentUser(path, "600");
        return path;
    }

    private static void RestrictToCurrentUser(string path, string mode)
    {
        if (Util.IsWindows)
            return;

        try
        {
            var startInfo = new ProcessStartInfo("chmod") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add(mode);
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            if (process is { ExitCode: not 0 })
                Util.Log($"Could not restrict access to '{path}': chmod exited with code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            Util.Log($"Could not restrict access to '{path}': {ex.Message}");
        }
    }
}
