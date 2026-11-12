using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCheck.DotNet
{
	/// <summary>
	/// Finds the workloads that installing another workload also installs. <c>dotnet workload list</c>
	/// names only the workloads installed by name: after <c>dotnet workload install maui</c> it lists
	/// <c>maui</c>, not <c>maui-android</c> or <c>maui-ios</c>, although their packs are installed
	/// (issue #614).
	/// </summary>
	internal static class IncludedWorkloads
	{
		/// <summary>
		/// The non-abstract <c>extends</c> entries of the Microsoft.NET.Sdk.Maui workload manifest,
		/// the only workloads in the uno-check manifests that include others. They are the same
		/// from the .NET 8 to the .NET 11 manifests. The SDK CLI doesn't report this graph.
		/// </summary>
		static readonly IReadOnlyDictionary<string, string[]> Extends = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
		{
			["maui"] = ["maui-mobile", "maui-desktop"],
			["maui-mobile"] = ["maui-android", "maui-ios", "maui-tizen"],
			["maui-desktop"] = ["maui-maccatalyst", "maui-windows"],
			["maui-android"] = ["android"],
			["maui-ios"] = ["ios"],
			["maui-maccatalyst"] = ["maccatalyst"],
		};

		/// <summary>
		/// The installed workload that provides <paramref name="workloadId"/>: the workload itself when
		/// it was installed by name, otherwise an installed workload that extends it, directly or through
		/// other workloads. Null when no installed workload provides it.
		/// </summary>
		internal static string FindInstalledProvider(string workloadId, IReadOnlyCollection<string> installedWorkloadIds)
		{
			if (installedWorkloadIds.Contains(workloadId, StringComparer.OrdinalIgnoreCase))
				return workloadId;

			return installedWorkloadIds.FirstOrDefault(installed => Includes(installed, workloadId));
		}

		static bool Includes(string workloadId, string includedWorkloadId)
			=> Extends.TryGetValue(workloadId, out var extended)
				&& extended.Any(e => string.Equals(e, includedWorkloadId, StringComparison.OrdinalIgnoreCase) || Includes(e, includedWorkloadId));
	}
}
