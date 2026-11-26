using DotNetCheck.Manifest;
using DotNetCheck.Models;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetCheck.Checkups
{
	public class DotNetSdkCheckupContributor : CheckupContributor
	{
		public override IEnumerable<Checkup> Contribute(Manifest.Manifest manifest, SharedState sharedState)
		{
			var sdks = manifest?.Check?.DotNet?.Sdks;

			if (sdks?.Any() ?? false)
			{
				foreach (var sdk in sdks)
				{
					var workloads = sdk?.Workloads?.ToArray() ?? Array.Empty<DotNetWorkload>();
					var pkgSrcs = sdk?.PackageSources?.ToArray() ?? Array.Empty<string>();

					// The checkup is identified by the manifest's SDK; the SDK that runs the workload commands
					// is selected when it examines (DotNetWorkloadsCheckup).
					var sdkVersion = sdk.Version;

					if (sdk.Workloads?.Any() ?? false)
					{
						yield return new DotNetWorkloadsCheckup(sharedState, sdkVersion, workloads, pkgSrcs);
					}
				}
			}
		}
	}
}
