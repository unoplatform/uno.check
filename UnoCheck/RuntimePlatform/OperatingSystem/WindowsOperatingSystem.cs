using DotNetCheck.RuntimePlatform.Core;
using DotNetCheck.RuntimePlatform.Model;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetCheck.RuntimePlatform.OperatingSystem
{
	public class WindowsOperatingSystem : OperatingSystemBase
	{
		public override IList<InstalledProgramInfo> GetInstalledPrograms()
		{
			if (_installedProgramsStore == null)
			{
				_installedProgramsStore = new List<InstalledProgramInfo>();
			}
			else
			{
				return _installedProgramsStore;
			}

			this.AppendProgramName(@"SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall", RegistryHive.LocalMachine);
			this.AppendProgramName(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryHive.LocalMachine);
			this.AppendProgramName(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", RegistryHive.CurrentUser);

			return _installedProgramsStore;
		}

		private void AppendProgramName(string registryKey, RegistryHive registryHive)
		{
			using (RegistryKey key = RegistryKey.OpenRemoteBaseKey(registryHive, Environment.MachineName).OpenSubKey(registryKey))
			{
				if (key is not null)
				{
					foreach (string subkey_name in key.GetSubKeyNames())
					{
						using (RegistryKey subkey = key.OpenSubKey(subkey_name))
						{
							if (subkey?.GetValue("DisplayName") != null)
							{
								_installedProgramsStore.Add(new InstalledProgramInfo(subkey.GetValue("DisplayName")?.ToString(), subkey.GetValue("Version")?.ToString()));
							}
						}
					}
				}
			}
		}

		public override bool IsProgramInstalled(string partialName) => this.GetInstalledPrograms().FirstOrDefault(p => p.Name.Contains(partialName)) is not null;

		private IList<InstalledProgramInfo> _installedProgramsStore;
	}
}