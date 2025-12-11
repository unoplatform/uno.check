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
			Frameworks = new[] { "net8.0-ios" },
			TargetPlatforms = new[] { "ios" }
		};
		var manifest = new Manifest
		{
			Check = new Check
			{
				ToolVersion = "2.0.0"
			}
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
			exitCode);

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
		Assert.Equal(new[] { androidCheckup.Id, jdkCheckup.Id }, report.Results.Select(r => r.Id));

		var androidResult = Assert.Single(report.Results, r => r.Id == androidCheckup.Id);
		Assert.Equal(Status.Warning, androidResult.Status);
		Assert.NotNull(androidResult.Suggestion);
		Assert.Equal("Install Android SDK", androidResult.Suggestion!.Name);
		Assert.Equal("Use Android SDK manager", androidResult.Suggestion.Description);
		Assert.True(androidResult.Suggestion.HasSolution);

		var jdkResult = Assert.Single(report.Results, r => r.Id == jdkCheckup.Id);
		Assert.Equal("JDK not found", jdkResult.Message);

		var skipInfo = Assert.Single(report.SkippedCheckups);
		Assert.Equal("git", skipInfo.Id);
		Assert.Equal("Skipped for test coverage", skipInfo.Reason);
		Assert.False(skipInfo.IsError);

		Assert.Equal(new[] { androidCheckup.Id }, report.SkippedFixes);
	}

	[Fact]
	public async Task CheckReportWriter_WritesReportToDisk()
	{
		// Arrange
		var report = new CheckReport
		{
			ToolVersion = "1.2.3",
			ManifestVersion = "9.9.9",
			ManifestChannel = "Preview",
			StartedAtUtc = DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
			CompletedAtUtc = DateTimeOffset.Parse("2024-01-01T00:00:05Z"),
			DurationSeconds = 5,
			ExitCode = 0,
			Platform = Platform.Windows,
			Frameworks = new[] { "net8.0-android" },
			TargetPlatforms = new[] { "android" },
			Results = new[]
			{
				new CheckResultReport
				{
					Id = "androidsdk",
					Title = "Android SDK",
					Status = Status.Ok
				}
			},
			SkippedCheckups = new[]
			{
				new SkippedCheckReport
				{
					Id = "git",
					Reason = "Not needed",
					IsError = false
				}
			},
			SkippedFixes = new[] { "androidsdk" },
			HasErrors = false,
			HasWarnings = false
		};

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
}
