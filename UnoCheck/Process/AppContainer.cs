using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DotNetCheck
{
	/// <summary>
	/// Whether uno-check runs in a Windows AppContainer. A sandboxed host such as Win32 app isolation runs its app, and
	/// every process the app starts, in one, and asks the user for each read outside the folders it was given.
	/// </summary>
	internal static class AppContainer
	{
		const uint TokenQuery = 0x0008;
		const int TokenIsAppContainer = 29;

		/// <summary>True when the current process runs in an AppContainer; false off Windows, or when it can't be told.</summary>
		internal static bool IsCurrentProcess { get; } = Util.IsWindows && ReadIsCurrentProcess();

		static bool ReadIsCurrentProcess()
		{
			if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
			{
				Util.Exception(new Win32Exception(Marshal.GetLastWin32Error()));
				return false;
			}

			try
			{
				if (!GetTokenInformation(token, TokenIsAppContainer, out var isAppContainer, sizeof(int), out _))
				{
					Util.Exception(new Win32Exception(Marshal.GetLastWin32Error()));
					return false;
				}

				Util.Log($"AppContainer: {isAppContainer != 0}");
				return isAppContainer != 0;
			}
			finally
			{
				CloseHandle(token);
			}
		}

		[DllImport("kernel32.dll")]
		static extern IntPtr GetCurrentProcess();

		[DllImport("advapi32.dll", SetLastError = true)]
		static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

		[DllImport("advapi32.dll", SetLastError = true)]
		static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool CloseHandle(IntPtr handle);
	}
}
