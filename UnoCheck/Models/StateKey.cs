using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck.Models
{
	public static class StateKey
	{
		public const string EntryPoint = "EntryPoint";

		public const string Skips = "Skips";

		public const string TargetPlatforms = "target_platforms";

		/// <summary>The <c>--tfm</c> values as given, a <c>string[]</c>. Not contributed when there are none.</summary>
		public const string TargetFrameworks = "target_frameworks";

		public const string ShouldRestartVs = "ShouldRestartVS";

		/// <summary>The SDK version asked for with <c>--dotnet-sdk-version</c>, a <c>string</c>. Not contributed when there is none.</summary>
		public const string DotNetSdkVersion = "dotnet_sdk_version";

		/// <summary>The <c>DotNetSdkSelection</c> the <c>dotnet</c> checkup made, contributed under that checkup's id.</summary>
		public const string DotNetSdkSelection = "dotnet_sdk_selection";
	}
}
