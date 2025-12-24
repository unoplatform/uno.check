#nullable enable

using DotNetCheck.Manifest;
using DotNetCheck.Models;
using DotNetCheck.Reporting;
using ManifestModel = DotNetCheck.Manifest.Manifest;

namespace DotNetCheck.Cli;

public class CheckCommandDetailTests
{
	private sealed class DetailCheckup(string id, string title) : Checkup
	{
		public override string Id => id;

		public override string Title => title;

		public override Task<DiagnosticResult> Examine(SharedState history) =>
			Task.FromResult(DiagnosticResult.Ok(this));
	}

	[Fact]
	public void HandleCheckupStatusUpdated_CapturesDetails_AndReportIncludesThem()
	{
		// Arrange
		var command = new CheckCommand();
		var checkup = new DetailCheckup("checkup", "Test Checkup");
		var first = new CheckupStatusEventArgs(checkup, "First detail", Status.Warning);
		var second = new CheckupStatusEventArgs(checkup, "Second detail", Status.Error);

		// Act
		command.HandleCheckupStatusUpdated(first);
		command.HandleCheckupStatusUpdated(second);
		var snapshot = command.SnapshotCheckupDetails();
		var report = CreateReport(checkup, snapshot);

		// Assert
		var result = Assert.Single(report.Results);
		Assert.NotNull(result.Details);
		Assert.Equal(2, result.Details!.Count);
		Assert.Equal("First detail", result.Details[0].Message);
		Assert.Equal(Status.Warning, result.Details[0].Status);
		Assert.Equal("Second detail", result.Details[1].Message);
		Assert.Equal(Status.Error, result.Details[1].Status);
	}

	[Fact]
	public void ResetCheckupDetailsForRun_ClearsDetailsOnRetry()
	{
		// Arrange
		var command = new CheckCommand();
		var checkup = new DetailCheckup("retry-checkup", "Retry Checkup");
		command.HandleCheckupStatusUpdated(new CheckupStatusEventArgs(checkup, "First detail", Status.Warning));

		// Act
		command.ResetCheckupDetailsForRun(checkup, isRetry: true);
		command.HandleCheckupStatusUpdated(new CheckupStatusEventArgs(checkup, "Retry detail", Status.Error));
		var snapshot = command.SnapshotCheckupDetails();

		// Assert
		Assert.True(snapshot.TryGetValue(checkup.Id, out var details));
		Assert.Single(details);
		Assert.Equal("Retry detail", details[0].Message);
	}

	private static CheckReport CreateReport(
		Checkup checkup,
		IDictionary<string, IReadOnlyList<CheckResultDetailReport>>? details)
	{
		var results = new Dictionary<string, DiagnosticResult>
		{
			[checkup.Id] = DiagnosticResult.Ok(checkup)
		};
		var settings = new CheckSettings
		{
			Frameworks = [],
			TargetPlatforms = []
		};
		var manifest = new ManifestModel
		{
			Check = new Check
			{
				ToolVersion = "1.0.0"
			}
		};

		return CheckReportFactory.Create(
			results,
			Array.Empty<SkipInfo>(),
			Array.Empty<string>(),
			settings,
			manifest,
			ManifestChannel.Default,
			DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
			TimeSpan.FromSeconds(1),
			Platform.Windows,
			exitCode: 0,
			details);
	}
}
