using System.Collections.Generic;
using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LinuxNinjaSolution : Solution
	{
		public static IEnumerable<(LinuxPackageManagerWrapper wrapper, string packageName)> NinjaPackageNamesWithWrappers { get; } =
			LinuxPackageManagerWrapper.MatchPackageNamesWithStandardSupport(
				"ninja-build",
				"ninja",
				"ninja-build",
				"ninja-build",
				"ninja"
			);
		
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);
			
			// no need to update first, we should've already searched for the package
			var installedPackage = await LinuxPackageManagerWrapper.InstallPackage(NinjaPackageNamesWithWrappers, false);

			if (installedPackage)
			{
				ReportStatus("ninja-build was installed on Linux.");
			}
			else
			{
				ReportStatus("ninja-build could not be installed.");
			}
		}
	}
}