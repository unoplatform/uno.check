using DotNetCheck.RuntimePlatform.Model;
using System.Collections.Generic;

namespace DotNetCheck.RuntimePlatform.Core
{
	public abstract class OperatingSystemBase
	{
		public virtual Platform Platform { get; } = Platform.Windows;

		public abstract IList<InstalledProgramInfo> GetInstalledPrograms();

		public abstract bool IsProgramInstalled(string partialName);
	}
}