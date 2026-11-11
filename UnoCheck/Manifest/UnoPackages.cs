using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	/// <summary>
	/// How uno-check resolves the Uno Platform packages it installs (Uno.Templates, Uno.Sdk).
	/// </summary>
	public partial class UnoPackages
	{
		/// <summary>
		/// Resolve to the latest prerelease instead of the latest stable version. Set it only while a
		/// manifest's .NET SDK needs an unreleased Uno Platform major.
		/// </summary>
		[JsonProperty("includePrerelease")]
		public bool IncludePrerelease { get; set; }
	}
}
