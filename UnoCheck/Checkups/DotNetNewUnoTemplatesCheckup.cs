#nullable enable

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using NuGet.Versioning;
using System.Linq;

namespace DotNetCheck.Checkups;

internal class DotNetNewUnoTemplatesCheckup : DotNetNewTemplatesCheckupBase
{
    public override string Id => "dotnetnewunotemplates";

    public override string Title => "dotnet new Uno Project Templates";

    public override string TemplatesDisplayName => "Uno Platform";

    public override string PackageName => "Uno.ProjectTemplates.Dotnet";

    public override Regex DotNetNewOutputRegex { get; } = new(
        pattern: @"Uno\.ProjectTemplates\.Dotnet\s*$\s*Version: (.*)",
        options: RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
}
