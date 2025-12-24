#nullable enable

using System.Text.Json;
using DotNetCheck;
using DotNetCheck.Manifest;
using DotNetCheck.Models;
using DotNetCheck.Reporting;

namespace UnoCheck.Tests;

public class ReportingTests
{
	private sealed class TestCheckup : Checkup
	{
		private readonly string _id;
		private readonly string _title;

		public TestCheckup(string id, string title)
		{
			_id = id;
			_title = title;
		}

		public override string Id => _id;

		public override string Title => _title;

		public override Task<DiagnosticResult> Examine(SharedState history) =>
			Task.FromResult(DiagnosticResult.Ok(this));
	}

	private sealed class TestSolution : Solution
	{
	}

	private sealed record CheckReportFactoryInputs(
		IDictionary<string, DiagnosticResult> Results,
		IEnumerable<SkipInfo> SkippedCheckups,
		IEnumerable<string> SkippedFixes,
		CheckSettings Settings,
		Manifest Manifest,
		ManifestChannel ManifestChannel,
		DateTimeOffset StartedAtUtc,
		TimeSpan Duration,
		Platform Platform,
		int ExitCode);

	[Fact]
	public void CheckReportFactory_CreatesExpectedReport()
	{
		// Arrange
		var androidCheckup = new TestCheckup("androidsdk", "Android SDK");
		var jdkCheckup = new TestCheckup("jdk", "OpenJDK");
		var results = new Dictionary<string, DiagnosticResult>
		{
			[androidCheckup.Id] = new DiagnosticResult(Status.Warning, androidCheckup, new Suggestion("Install Android SDK", "Use Android SDK manager", new TestSolution())),
			[jdkCheckup.Id] = new DiagnosticResult(Status.Error, jdkCheckup, "JDK not found")
		};
		var skippedCheckups = new[] { new SkipInfo("git", "Skipped for test coverage", false) };
		var skippedFixes = new[] { androidCheckup.Id, androidCheckup.Id };
		var settings = new CheckSettings
		{
			Frameworks = ["net8.0-ios"],
			TargetPlatforms = ["ios"]
		};
		var manifest = new Manifest
		{
			Check = new Check
			{
				ToolVersion = "2.0.0"
			}
		};
		var details = new Dictionary<string, IReadOnlyList<CheckResultDetailReport>>
		{
			[androidCheckup.Id] =
			[
				new CheckResultDetailReport("Android SDK missing", Status.Warning)
			],
			[jdkCheckup.Id] =
			[
				new CheckResultDetailReport("No JDK found", Status.Error)
			]
		};
		var startedAtUtc = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
		var duration = TimeSpan.FromSeconds(5);
		var exitCode = 1;

		// Act
		var report = CheckReportFactory.Create(
			results,
			skippedCheckups,
			skippedFixes,
			settings,
			manifest,
			ManifestChannel.Preview,
			startedAtUtc,
			duration,
			Platform.OSX,
			exitCode,
			details);

		// Assert
		Assert.Equal(ToolInfo.CurrentVersion.ToNormalizedString(), report.ToolVersion);
		Assert.Equal("2.0.0", report.ManifestVersion);
		Assert.Equal("preview", report.ManifestChannel);
		Assert.Equal(startedAtUtc, report.StartedAtUtc);
		Assert.Equal(startedAtUtc + duration, report.CompletedAtUtc);
		Assert.Equal(duration.TotalSeconds, report.DurationSeconds);
		Assert.Equal(exitCode, report.ExitCode);
		Assert.Equal(Platform.OSX, report.Platform);
		Assert.Equal(settings.Frameworks, report.Frameworks);
		Assert.Equal(settings.TargetPlatforms, report.TargetPlatforms);
		Assert.True(report.HasErrors);
		Assert.True(report.HasWarnings);
		Assert.Equal([androidCheckup.Id, jdkCheckup.Id], report.Results.Select(r => r.Id));

		var androidResult = Assert.Single(report.Results, r => r.Id == androidCheckup.Id);
		Assert.Equal(Status.Warning, androidResult.Status);
		Assert.NotNull(androidResult.Details);
		Assert.Single(androidResult.Details!);
		Assert.Equal("Android SDK missing", androidResult.Details![0].Message);
		Assert.Equal(Status.Warning, androidResult.Details[0].Status);
		Assert.NotNull(androidResult.Suggestion);
		Assert.Equal("Install Android SDK", androidResult.Suggestion!.Name);
		Assert.Equal("Use Android SDK manager", androidResult.Suggestion.Description);
		Assert.True(androidResult.Suggestion.HasSolution);

		var jdkResult = Assert.Single(report.Results, r => r.Id == jdkCheckup.Id);
		Assert.Equal("JDK not found", jdkResult.Message);
		Assert.NotNull(jdkResult.Details);
		Assert.Single(jdkResult.Details!);
		Assert.Equal("No JDK found", jdkResult.Details![0].Message);
		Assert.Equal(Status.Error, jdkResult.Details[0].Status);

		Assert.Equal([androidCheckup.Id, jdkCheckup.Id], report.UnresolvedCheckups.Select(r => r.Id));

		var androidUnresolved = Assert.Single(report.UnresolvedCheckups, r => r.Id == androidCheckup.Id);
		Assert.Equal(Status.Warning, androidUnresolved.Status);
		Assert.Equal(FixStatus.Skipped, androidUnresolved.FixStatus);
		Assert.Equal("Automatic fix was available but not attempted.", androidUnresolved.Reason);

		var jdkUnresolved = Assert.Single(report.UnresolvedCheckups, r => r.Id == jdkCheckup.Id);
		Assert.Equal(Status.Error, jdkUnresolved.Status);
		Assert.Equal(FixStatus.NotAvailable, jdkUnresolved.FixStatus);
		Assert.Equal("No automatic fix is available for this checkup.", jdkUnresolved.Reason);

		var skipInfo = Assert.Single(report.SkippedCheckups);
		Assert.Equal("git", skipInfo.Id);
		Assert.Equal("Skipped for test coverage", skipInfo.Reason);
		Assert.False(skipInfo.IsError);

		Assert.Equal([androidCheckup.Id], report.SkippedFixes);
	}

	[Fact]
	public void CheckReportFactory_Create_ThrowsWhenResultsNull()
	{
		// Arrange
		var inputs = CreateFactoryInputs();

		// Act
		var exception = Assert.Throws<ArgumentNullException>(() => CheckReportFactory.Create(
			null!,
			inputs.SkippedCheckups,
			inputs.SkippedFixes,
			inputs.Settings,
			inputs.Manifest,
			inputs.ManifestChannel,
			inputs.StartedAtUtc,
			inputs.Duration,
			inputs.Platform,
			inputs.ExitCode));

		// Assert
		Assert.Equal("results", exception.ParamName);
	}

	[Fact]
	public void CheckReportFactory_Create_ThrowsWhenSkippedCheckupsNull()
	{
		// Arrange
		var inputs = CreateFactoryInputs();

		// Act
		var exception = Assert.Throws<ArgumentNullException>(() => CheckReportFactory.Create(
			inputs.Results,
			null!,
			inputs.SkippedFixes,
			inputs.Settings,
			inputs.Manifest,
			inputs.ManifestChannel,
			inputs.StartedAtUtc,
			inputs.Duration,
			inputs.Platform,
			inputs.ExitCode));

		// Assert
		Assert.Equal("skippedCheckups", exception.ParamName);
	}

	[Fact]
	public void CheckReportFactory_Create_ThrowsWhenSkippedFixesNull()
	{
		// Arrange
		var inputs = CreateFactoryInputs();

		// Act
		var exception = Assert.Throws<ArgumentNullException>(() => CheckReportFactory.Create(
			inputs.Results,
			inputs.SkippedCheckups,
			null!,
			inputs.Settings,
			inputs.Manifest,
			inputs.ManifestChannel,
			inputs.StartedAtUtc,
			inputs.Duration,
			inputs.Platform,
			inputs.ExitCode));

		// Assert
		Assert.Equal("skippedFixes", exception.ParamName);
	}

	[Fact]
	public void CheckReportFactory_Create_ThrowsWhenSettingsNull()
	{
		// Arrange
		var inputs = CreateFactoryInputs();

		// Act
		var exception = Assert.Throws<ArgumentNullException>(() => CheckReportFactory.Create(
			inputs.Results,
			inputs.SkippedCheckups,
			inputs.SkippedFixes,
			null!,
			inputs.Manifest,
			inputs.ManifestChannel,
			inputs.StartedAtUtc,
			inputs.Duration,
			inputs.Platform,
			inputs.ExitCode));

		// Assert
		Assert.Equal("settings", exception.ParamName);
	}

	[Fact]
	public async Task CheckReportWriter_WritesReportToDisk()
	{
		// Arrange
		var report = new CheckReport(
			ToolVersion: "1.2.3",
			ManifestVersion: "9.9.9",
			ManifestChannel: "preview",
			StartedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
			CompletedAtUtc: DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
			DurationSeconds: 5,
			ExitCode: 0,
			Platform: Platform.Windows,
			Frameworks: ["net8.0-android"],
			TargetPlatforms: ["android"],
			Results:
			[
				new CheckResultReport(
					Id: "androidsdk",
					Title: "Android SDK",
					Status: Status.Ok,
					Message: null,
					Details:
					[
						new CheckResultDetailReport("All good", Status.Ok)
					],
					Suggestion: null)
			],
			SkippedCheckups:
			[
				new SkippedCheckReport("git", "Not needed", false)
			],
			SkippedFixes: ["androidsdk"],
			UnresolvedCheckups: Array.Empty<UnresolvedCheckReport>(),
			HasErrors: false,
			HasWarnings: false);

		var reportDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		var reportPath = Path.Combine(reportDirectory, "report.json");

		try
		{
			// Act
			await CheckReportWriter.WriteReportAsync(report, reportPath);

			// Assert
			Assert.True(File.Exists(reportPath));
			var json = await File.ReadAllTextAsync(reportPath);
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;

			Assert.Equal("1.2.3", root.GetProperty("toolVersion").GetString());
			Assert.Equal("preview", root.GetProperty("manifestChannel").GetString());
			Assert.Equal("windows", root.GetProperty("platform").GetString());

			var results = root.GetProperty("results");
			Assert.Equal(JsonValueKind.Array, results.ValueKind);
			var firstResult = results[0];
			Assert.Equal("androidsdk", firstResult.GetProperty("id").GetString());
			Assert.Equal("ok", firstResult.GetProperty("status").GetString());

			var resultDetails = firstResult.GetProperty("details");
			Assert.Equal(JsonValueKind.Array, resultDetails.ValueKind);
			Assert.Equal("All good", resultDetails[0].GetProperty("message").GetString());
			Assert.Equal("ok", resultDetails[0].GetProperty("status").GetString());

			var skipped = root.GetProperty("skippedCheckups");
			Assert.Equal("git", skipped[0].GetProperty("id").GetString());
			Assert.Equal("Not needed", skipped[0].GetProperty("reason").GetString());
		}
		finally
		{
			if (Directory.Exists(reportDirectory))
			{
				Directory.Delete(reportDirectory, true);
			}
		}
	}

	[Fact]
	public async Task CheckReportWriter_WriteReportAsync_ThrowsWhenReportNull()
	{
		// Arrange
		CheckReport? report = null;

		// Act
		var exception = await Assert.ThrowsAsync<ArgumentNullException>(
			() => CheckReportWriter.WriteReportAsync(report!));

		// Assert
		Assert.Equal("report", exception.ParamName);
	}

	[Fact]
	public async Task CheckReportWriter_WriteReportAsync_ThrowsWhenReportPathEmpty()
	{
		// Arrange
		var report = CreateReport(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

		// Act
		var exception = await Assert.ThrowsAsync<ArgumentException>(
			() => CheckReportWriter.WriteReportAsync(report, string.Empty));

		// Assert
		Assert.Equal("reportPath", exception.ParamName);
	}

	[Fact]
	public async Task CheckReportWriter_WriteReportAsync_ThrowsWhenReportPathHasNoDirectory()
	{
		// Arrange
		var report = CreateReport(DateTimeOffset.Parse("2024-01-01T00:00:00Z"));

		// Act
		var exception = await Assert.ThrowsAsync<InvalidOperationException>(
			() => CheckReportWriter.WriteReportAsync(report, "report.json"));

		// Assert
		Assert.Contains("report.json", exception.Message);
	}

	[Fact]
	public async Task CheckReportWriter_TrimsOldReports()
	{
		var reportDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(reportDirectory);

		try
		{
			var startedAtUtc = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

			for (var i = 0; i < 60; i++)
			{
				var report = CreateReport(startedAtUtc.AddMinutes(i));
				var reportPath = Path.Combine(reportDirectory, $"report-{i:D4}.json");

				await CheckReportWriter.WriteReportAsync(report, reportPath, enableCleanup: true);
			}

			var reportFiles = Directory.GetFiles(reportDirectory, "report-*.json");
			Assert.Equal(50, reportFiles.Length);
		}
		finally
		{
			if (Directory.Exists(reportDirectory))
			{
				Directory.Delete(reportDirectory, true);
			}
		}
	}

	private static CheckReport CreateReport(DateTimeOffset startedAtUtc) =>
		new(
			ToolVersion: "1.2.3",
			ManifestVersion: "9.9.9",
			ManifestChannel: "preview",
			StartedAtUtc: startedAtUtc,
			CompletedAtUtc: startedAtUtc.AddSeconds(5),
			DurationSeconds: 5,
			ExitCode: 0,
			Platform: Platform.Windows,
			Frameworks: ["net8.0-android"],
			TargetPlatforms: ["android"],
			Results: Array.Empty<CheckResultReport>(),
			SkippedCheckups: Array.Empty<SkippedCheckReport>(),
			SkippedFixes: Array.Empty<string>(),
			UnresolvedCheckups: Array.Empty<UnresolvedCheckReport>(),
			HasErrors: false,
			HasWarnings: false);

	private static CheckReportFactoryInputs CreateFactoryInputs()
	{
		var checkup = new TestCheckup("test", "Test Checkup");

		return new CheckReportFactoryInputs(
			new Dictionary<string, DiagnosticResult>
			{
				[checkup.Id] = DiagnosticResult.Ok(checkup)
			},
			[],
			[],
			new CheckSettings
			{
				Frameworks = [],
				TargetPlatforms = []
			},
			new Manifest
			{
				Check = new Check
				{
					ToolVersion = "1.0.0"
				}
			},
			ManifestChannel.Default,
			DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
			TimeSpan.FromSeconds(1),
			Platform.Windows,
			0);
	}
}
