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
	}
}
