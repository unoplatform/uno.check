using System;
using System.Linq;
using DotNetCheck.Solutions;
using Xunit;

namespace UnoCheck.Tests
{
	/// <summary>
	/// Tests for the workload-update failure message (spec 001): the exit code alone is
	/// not actionable, so the message must carry the tail of the process output.
	/// </summary>
	public class DotNetWorkloadUpdateSolutionTests
	{
		[Fact]
		public void BuildFailureMessage_NoOutput_ExitCodeOnly()
		{
			var message = DotNetWorkloadUpdateSolution.BuildFailureMessage("/roots/dotnet", 1, Array.Empty<string>());

			Assert.Equal("'/roots/dotnet workload update' exited with code 1.", message);
		}

		[Fact]
		public void BuildFailureMessage_WithOutput_CarriesTheActionableReason()
		{
			var message = DotNetWorkloadUpdateSolution.BuildFailureMessage(
				"/roots/dotnet", 1, new[] { "Updating workloads...", "", "Workload update failed: access to the path is denied." });

			Assert.Contains("exited with code 1", message);
			Assert.Contains("access to the path is denied", message);
			Assert.DoesNotContain(Environment.NewLine + Environment.NewLine, message); // blank lines dropped
		}

		[Fact]
		public void BuildFailureMessage_LongOutput_KeepsOnlyTheTail()
		{
			var lines = Enumerable.Range(1, 50).Select(i => $"line {i}").ToArray();

			var message = DotNetWorkloadUpdateSolution.BuildFailureMessage("/roots/dotnet", 1, lines);

			Assert.DoesNotContain("line 40", message);
			Assert.Contains("line 41", message);
			Assert.Contains("line 50", message);
		}
	}
}
