#nullable enable

using DotNetCheck.Manifest;
using DotNetCheck.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Checks that requisite VS workloads are installed
	/// </summary>
	public class VSWinWorkloadsCheckup : Checkup
	{
		public override string Id => "vswinworkloads";

		public override string Title => "Visual Studio Workloads";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override bool ShouldExamine(SharedState history) => Manifest?.Check?.VSWindows?.Workloads != null;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			if (!(Manifest?.Check?.VSWindows?.Workloads is { } workloads))
			{
				return DiagnosticResult.Ok(this);
			}

			var missingWorkloads = new List<VSWinWorkload>();
			foreach (var workload in workloads)
			{
				if (workload.Id == null)
				{
					continue;
				}

				var supportedInstalls = await VisualStudioWindowsCheckup.GetWindowsInfo(workload.Id);
				if (supportedInstalls.Any())
				{
					var installsStr = string.Join(", ", supportedInstalls.Select(vs => vs.Version));
					ReportStatus($"{workload.Name ?? workload.Id} is installed ({installsStr})", Status.Ok);
				}
				else
				{
					ReportStatus($"{workload.Name ?? workload.Id} is not installed", Status.Error);
					missingWorkloads.Add(workload);
				}
			}

			if (missingWorkloads.Count == 0)
			{
				return DiagnosticResult.Ok(this);
			}
			else
			{
				// TODO: automatically install with vs_installer

				var sb = new StringBuilder();
				var result = new DiagnosticResult(
					Status.Error,
					this,
					prescription: new Suggestion("Install missing workloads", 
					"Some required workloads were not found. You should run Visual Studio Installer to install the missing workloads.")
				);
				return result;
			}
		}
	}
}
