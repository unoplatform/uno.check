using DotNetCheck.Json;
using Client = Uno.Check.Client;

namespace UnoCheck.Tests;

/// <summary>
/// Pins the CLI's writer against the shipped reader (<c>Uno.Check.Client</c>). The client
/// cannot share the writer's types — the CLI still targets up to net6.0 — so the two are
/// mirrors, and mirrors drift: an earlier draft named the catalog title <c>title</c> while a
/// consumer read <c>name</c>, and nothing caught it.
///
/// Every test here renders a real writer event and parses it with the real client, so a
/// rename that compiles on one side fails here rather than in a host at runtime.
/// </summary>
public class ClientContractTests
{
	private static Client.UnoCheckEvent? RoundTrip(JsonlEvent evt)
	{
		var line = JsonlOutput.Render(evt);
		var parsed = Client.UnoCheckProtocol.TryParseLine(line);
		Assert.NotNull(parsed);
		return Client.UnoCheckProtocol.Map(parsed!);
	}

	[Fact]
	public void RunStarted_CarriesTheCheckupCount()
	{
		var evt = RoundTrip(new RunStartedEvent { CheckupCount = 19 });

		Assert.Equal(19, Assert.IsType<Client.UnoCheckRunStarted>(evt).CheckupCount);
	}

	[Fact]
	public void CheckupStarted_CarriesIdAndName()
	{
		var evt = RoundTrip(new CheckupStartedEvent { Id = "openjdk", Name = "OpenJDK 17" });

		var started = Assert.IsType<Client.UnoCheckCheckStarted>(evt);
		Assert.Equal("openjdk", started.Id);
		Assert.Equal("OpenJDK 17", started.Name);
	}

	[Fact]
	public void CheckupResult_CarriesStatusMessageAndFixability()
	{
		var evt = RoundTrip(new CheckupResultEvent
		{
			Check = new HealthCheck
			{
				Id = "windowshyperv",
				Name = "Windows Hyper-V",
				Status = "warning",
				Message = "Activate Hyper-V",
				Fix = new FixInfo { IssueId = "windowshyperv", AutoFixable = true, RequiresElevation = true },
			},
		});

		var result = Assert.IsType<Client.UnoCheckCheckResult>(evt);
		Assert.Equal("windowshyperv", result.Id);
		Assert.Equal("warning", result.Status);
		Assert.Equal("Activate Hyper-V", result.Message);
		Assert.True(result.Fixable);
		Assert.True(result.RequiresElevation);
	}

	[Fact]
	public void CheckupResult_UserScopedFix_ReadsAsNotRequiringElevation()
	{
		var evt = RoundTrip(new CheckupResultEvent
		{
			Check = new HealthCheck
			{
				Id = "unosdk",
				Name = "Uno SDK",
				Status = "error",
				Fix = new FixInfo { IssueId = "unosdk", AutoFixable = true, RequiresElevation = false },
			},
		});

		// A host batches fixes by this flag; reading false as true would elevate work that
		// only writes user state, and reading true as false would fail the fix outright.
		Assert.False(Assert.IsType<Client.UnoCheckCheckResult>(evt).RequiresElevation);
	}

	[Fact]
	public void CheckupResult_WithoutFix_IsNotFixable()
	{
		var evt = RoundTrip(new CheckupResultEvent
		{
			Check = new HealthCheck { Id = "edgewebview2", Name = "Edge WebView2", Status = "error" },
		});

		Assert.False(Assert.IsType<Client.UnoCheckCheckResult>(evt).Fixable);
	}

	[Fact]
	public void CheckupResult_Skipped_CarriesItsReason()
	{
		var evt = RoundTrip(new CheckupResultEvent
		{
			Check = new HealthCheck
			{
				Id = "git",
				Name = "Git",
				Status = "skipped",
				SkipReason = "Not required by the current configuration",
			},
		});

		Assert.Equal("Not required by the current configuration", Assert.IsType<Client.UnoCheckCheckResult>(evt).SkipReason);
	}

	[Fact]
	public void FixResult_CarriesSuccessAndError()
	{
		var evt = RoundTrip(new FixResultEvent { Id = "unosdk", Success = false, Error = "restore failed" });

		var result = Assert.IsType<Client.UnoCheckFixResult>(evt);
		Assert.False(result.Success);
		Assert.Equal("restore failed", result.Error);
	}

	[Fact]
	public void Report_CarriesSummaryCounts()
	{
		var evt = RoundTrip(new ReportEvent
		{
			Report = new Report
			{
				Status = "unhealthy",
				Summary = new Summary { Total = 19, Ok = 13, Warning = 1, Error = 1, Skipped = 4 },
			},
		});

		var completed = Assert.IsType<Client.UnoCheckRunCompleted>(evt);
		Assert.Equal("unhealthy", completed.Status);
		Assert.Equal(19, completed.Total);
		Assert.Equal(13, completed.Ok);
		Assert.Equal(1, completed.Warning);
		Assert.Equal(1, completed.Error);
		Assert.Equal(4, completed.Skipped);
	}

	[Fact]
	public void Report_CarriesTheFailureReason()
	{
		// How an unknown --only id reaches a host: the run fails rather than passing empty,
		// and the host needs the reason to say why.
		var evt = RoundTrip(new ReportEvent
		{
			Report = new Report
			{
				Status = "unhealthy",
				Reason = "unknown checkup id(s) for --only: dotnetworkloads",
				Summary = new Summary(),
			},
		});

		Assert.Equal(
			"unknown checkup id(s) for --only: dotnetworkloads",
			Assert.IsType<Client.UnoCheckRunCompleted>(evt).Reason);
	}

	[Fact]
	public void CheckupCatalog_MapsIdAndDisplayName()
	{
		var line = JsonlOutput.Render(new CheckupCatalogEvent
		{
			Checkups =
			[
				new CheckupCatalogItem { Id = "openjdk", Name = "OpenJDK 17", TypeName = "OpenJdkCheckup" },
				new CheckupCatalogItem { Id = "dotnet", Name = ".NET SDK", TypeName = "DotNetCheckup" },
			],
		});

		var parsed = Client.UnoCheckProtocol.TryParseLine(line);
		Assert.NotNull(parsed);
		var entries = Client.UnoCheckProtocol.MapCatalog(parsed!);

		Assert.Equal(2, entries.Count);
		Assert.Equal("openjdk", entries[0].Id);
		// Guards the drift this suite exists for: the title lives in "name", not "title".
		Assert.Equal("OpenJDK 17", entries[0].Title);
		Assert.Equal(".NET SDK", entries[1].Title);
	}
}
