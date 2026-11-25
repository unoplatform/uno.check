#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using NuGet.Configuration;

namespace DotNetCheck.DotNet;

/// <summary>
/// uno-check runs some .NET commands from a scratch directory, where the user's nuget.config files are not
/// found. This writes a nuget.config there that carries the sources resolved for the user's directory.
/// </summary>
/// <remarks>
/// Credentials are deliberately not copied: values can be expanded from environment variables, and the scratch
/// directory is not private. Credentials stored in user-level settings still apply, because NuGet matches them
/// by source name.
/// </remarks>
internal static class ScratchNuGetConfig
{
    public const string FileName = "nuget.config";

    public static string Write(string directory, IEnumerable<PackageSource> sources, string? globalPackagesFolder = null)
    {
        var packageSources = new XElement("packageSources", new XElement("clear"));
        foreach (var source in sources)
        {
            var add = new XElement("add", new XAttribute("key", source.Name), new XAttribute("value", source.Source));
            if (source.ProtocolVersion != PackageSource.DefaultProtocolVersion)
                add.Add(new XAttribute("protocolVersion", source.ProtocolVersion));

            packageSources.Add(add);
        }

        var configuration = new XElement("configuration", packageSources, new XElement("disabledPackageSources", new XElement("clear")));
        if (!string.IsNullOrEmpty(globalPackagesFolder))
        {
            configuration.Add(new XElement("config",
                new XElement("add", new XAttribute("key", "globalPackagesFolder"), new XAttribute("value", globalPackagesFolder))));
        }

        var path = Path.Combine(directory, FileName);
        new XDocument(new XDeclaration("1.0", "utf-8", null), configuration).Save(path);
        return path;
    }
}
