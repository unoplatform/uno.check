using DotNetCheck.Models;
using Microsoft.Win32;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LongPathsNotEnabledSolution : Solution
	{
		private readonly string _keyPath;
		private readonly string _valueName;

		public LongPathsNotEnabledSolution(string keyPath, string valueName)
		{
			_keyPath = keyPath;
			_valueName = valueName;
		}

		/// <summary>Writes under HKEY_LOCAL_MACHINE, which is machine scope.</summary>
		public override bool RequiresElevation => true;

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			// The writable handle is opened here rather than handed over by the checkup: the
			// checkup runs unelevated and only reads, so it has no write access to pass on.
			using var fileSystemKey = Registry.LocalMachine.OpenSubKey(_keyPath, writable: true)
				?? throw new InvalidOperationException(
					$@"Could not open HKEY_LOCAL_MACHINE\{_keyPath} for writing.");

			fileSystemKey.SetValue(_valueName, 1);

			ReportStatus("The long paths are enabled, you will need to reboot your machine for the change to take effect.");
		}
	}
}
