using System;
using System.Threading.Tasks;

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;

namespace DotNetCheck.Checkups
{
	internal class PSExecutionPolicyCheckup : Checkup
	{
		// What Windows PowerShell 5.1's Get-ExecutionPolicy -Scope CurrentUser reads. Read directly: starting Windows
		// PowerShell makes a sandboxed host ask the user for the default profile's folder.
		internal const string CurrentUserPolicyKey = @"Software\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell";
		internal const string PolicyValueName = "ExecutionPolicy";

		const string RequiredPolicy = "RemoteSigned";

		public override string Id => "psexecpolicy";

		public override string Title => $"PowerShell Execution Policy";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override Task<DiagnosticResult> Examine(SharedState state)
		{
			// For the PowerShell runs that follow, the fix and the SDK install.
			// See: https://github.com/PowerShell/PowerShell/issues/18530#issuecomment-1325691850
			Environment.SetEnvironmentVariable("PSModulePath", string.Empty);

			return Task.FromResult(PolicyResult(ReadCurrentUserPolicy()));
		}

		// Null when the current user has no policy of their own, which PowerShell reports as Undefined.
		static string ReadCurrentUserPolicy()
		{
#pragma warning disable CA1416 // Examined on Windows only (IsPlatformSupported).
			using var key = Registry.CurrentUser.OpenSubKey(CurrentUserPolicyKey);
			return key?.GetValue(PolicyValueName) as string;
#pragma warning restore CA1416
		}

		internal DiagnosticResult PolicyResult(string currentUserPolicy)
		{
			if (string.Equals(currentUserPolicy, RequiredPolicy, StringComparison.OrdinalIgnoreCase))
				return DiagnosticResult.Ok(this);

			return new DiagnosticResult(
				Status.Error,
				this,
				"PowerShell execution policy for current user is not RemoteSigned.",
				new("Change PowerShell execution policy to RemoteSigned", new PSExecutionPolicySolution()));
		}
	}
}
