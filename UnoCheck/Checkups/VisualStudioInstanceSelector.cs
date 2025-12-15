#nullable enable

using System.Collections.Generic;

namespace DotNetCheck.Checkups;

internal static class VisualStudioInstanceSelector
{
	internal const int MinimumSupportedMajor = 17;

	internal static bool TryGetLatestSupportedInstance(
		IReadOnlyList<VisualStudioInfo> instances,
		out VisualStudioInfo selected)
	{
		selected = default;

		if (instances is not { Count: > 0 })
		{
			return false;
		}

		var hasCandidate = false;
		foreach (var candidate in instances)
		{
			if (candidate.Version is not { } version)
			{
				continue;
			}

			if (version.Major < MinimumSupportedMajor)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(candidate.Path) || string.IsNullOrWhiteSpace(candidate.InstanceId))
			{
				continue;
			}

			if (!hasCandidate)
			{
				selected = candidate;
				hasCandidate = true;
				continue;
			}

			if (version > selected.Version)
			{
				selected = candidate;
			}
		}

		return hasCandidate;
	}
}

