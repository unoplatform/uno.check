using DotNetCheck.Models;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LongPathsNotEnabledSolution : Solution
	{
		public LongPathsNotEnabledSolution(RegistryKey fileSystemKey, string keyName) 
		{
			_fileSystemKey = fileSystemKey;
			_keyName = keyName;
		}

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			_fileSystemKey.SetValue(_keyName, 1);

			ReportStatus("The long paths are enabled, you will need to reboot your machine for the change to take effect.");
		}

		private RegistryKey _fileSystemKey;
		private string _keyName;
	}
}