#nullable enable

using System.Text.RegularExpressions;

namespace DotNetCheck.Checkups;

internal class DotNetNewExtensionsTemplatesCheckup : DotNetNewTemplatesCheckupBase
{
    public override string Id => "dotnetnewextensionstemplates";

    public override string Title => "dotnet new Uno Extensions Templates";

    public override string PackageName => "Uno.Extensions.Templates";

    public override string TemplatesDisplayName => "Uno Extensions";

    public override Regex DotNetNewOutputRegex { get; } = new(
        pattern: @"Uno\.Extensions\.Templates\s*$\s*Version: (.*)",
        options: RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
}
