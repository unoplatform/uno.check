using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	/// <summary>
	/// The manifest's <c>xcode</c> section. <see cref="MinExactVersion"/> holds the Xcode for the manifest's own
	/// .NET target framework. <see cref="TargetFrameworks"/> holds the Xcode for older ones (issue #618).
	/// </summary>
	public class XCodeRequirement : MinExactVersion
	{
		/// <summary>
		/// Maps an older .NET target framework without its platform (<c>net10.0</c>) to the Xcode major.minor
		/// that its Apple SDK packs require (<c>26.0</c>). The .NET SDK builds an older Apple target framework
		/// (<c>net10.0-ios</c> on the .NET 11 SDK) with that framework's pack from the current workloads, and the
		/// pack accepts only the Xcode major.minor in its <c>_RecommendedXcodeVersion</c>, older or newer.
		/// </summary>
		[JsonProperty("targetFrameworks")]
		public Dictionary<string, string> TargetFrameworks { get; set; }
	}
}
