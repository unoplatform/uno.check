using System.ComponentModel;
using Uno.Check.Client;
using Xunit;

namespace UnoCheck.Tests;

/// <summary>
/// Pins the JSONL wire contract (uno.check spec 003, schema 1.0) hosts consume.
/// The lines below are verbatim shapes the CLI emits: a field rename on either side must
/// fail here rather than degrade silently into empty rows or a status stuck on "running".
/// </summary>
public class ClientWireTests
{
	private static UnoCheckEvent? MapLine(string json)
	{
		var parsed = UnoCheckProtocol.TryParseLine(json);
		Assert.NotNull(parsed);
		return UnoCheckProtocol.Map(parsed!);
	}

	[Fact]
	[Description("run_started carries the denominator of the strip's \"N of M complete\" progress.")]
	public void RunStarted_CarriesCheckupCount()
	{
		var evt = MapLine("""{"type":"run_started","tool_version":"1.35.0","checkup_count":19}""");

		Assert.Equal(new UnoCheckRunStarted(19), evt);
	}

	[Fact]
	[Description("checkup_result reads the nested check object; reading the envelope instead yields blank rows.")]
	public void CheckupResult_ReadsNestedCheckObject()
	{
		var evt = MapLine("""
			{"type":"checkup_result","check":{"id":"openjdk","name":"OpenJDK 17.0.16","status":"ok","message":"Found"}}
			""");

		var result = Assert.IsType<UnoCheckCheckResult>(evt);
		Assert.Equal("openjdk", result.Id);
		Assert.Equal("OpenJDK 17.0.16", result.Name);
		Assert.Equal("ok", result.Status);
		Assert.Equal("Found", result.Message);
		Assert.False(result.Fixable);
	}

	[Fact]
	[Description("Fixable comes from fix.auto_fixable — the flag that decides whether a row shows a Fix button at all.")]
	public void CheckupResult_AutoFixableDrivesFixability()
	{
		var evt = MapLine("""
			{"type":"checkup_result","check":{"id":"windowshyperv","name":"Windows Hyper-V","status":"warning","fix":{"issue_id":"windowshyperv","auto_fixable":true}}}
			""");

		Assert.True(Assert.IsType<UnoCheckCheckResult>(evt).Fixable);
	}

	[Fact]
	[Description("A non-auto-fixable suggestion must not render a Fix button that cannot work.")]
	public void CheckupResult_NonAutoFixableIsNotFixable()
	{
		var evt = MapLine("""
			{"type":"checkup_result","check":{"id":"xcode","name":"Xcode","status":"error","fix":{"issue_id":"xcode","auto_fixable":false}}}
			""");

		Assert.False(Assert.IsType<UnoCheckCheckResult>(evt).Fixable);
	}

	[Fact]
	[Description("skip_reason is what a skipped row displays; dropping it renders a bare title with no explanation.")]
	public void CheckupResult_CarriesSkipReason()
	{
		var evt = MapLine("""
			{"type":"checkup_result","check":{"id":"git","name":"Git","status":"skipped","skip_reason":"Not required by the current configuration"}}
			""");

		var result = Assert.IsType<UnoCheckCheckResult>(evt);
		Assert.Equal("skipped", result.Status);
		Assert.Equal("Not required by the current configuration", result.SkipReason);
	}

	[Fact]
	[Description("A result with no status is treated as an error rather than silently rendering as passed.")]
	public void CheckupResult_MissingStatusDefaultsToError()
	{
		var evt = MapLine("""{"type":"checkup_result","check":{"id":"mystery","name":"Mystery"}}""");

		Assert.Equal("error", Assert.IsType<UnoCheckCheckResult>(evt).Status);
	}

	[Fact]
	[Description("The terminal report's summary drives the final strip counts — the page's only source for them.")]
	public void Report_CarriesStatusAndSummaryCounts()
	{
		var evt = MapLine("""
			{"type":"report","report":{"status":"unhealthy","summary":{"total":19,"ok":13,"warning":1,"error":1,"skipped":4}}}
			""");

		Assert.Equal(new UnoCheckRunCompleted("unhealthy", null, 19, 13, 1, 1, 4), evt);
	}

	[Fact]
	[Description("An abnormal end carries a reason; the strip shows it instead of pretending the run completed normally.")]
	public void Report_CarriesAbnormalReason()
	{
		var evt = MapLine("""
			{"type":"report","report":{"status":"unhealthy","reason":"manifest validation failed","summary":{"total":0,"ok":0,"warning":0,"error":0,"skipped":0}}}
			""");

		Assert.Equal("manifest validation failed", Assert.IsType<UnoCheckRunCompleted>(evt).Reason);
	}

	[Fact]
	[Description("fix_result carries success/error so a declined authorization surfaces on the card instead of looking like a silent no-op.")]
	public void FixResult_CarriesFailureDetail()
	{
		var evt = MapLine("""
			{"type":"fix_result","id":"windowshyperv","success":false,"error":"Administrator approval was declined."}
			""");

		Assert.Equal(new UnoCheckFixResult("windowshyperv", false, "Administrator approval was declined."), evt);
	}

	[Fact]
	[Description("checkup_started opens a row before its result arrives, which is what makes the list stream rather than appear at the end.")]
	public void CheckupStarted_OpensARow()
		=> Assert.Equal(
			new UnoCheckCheckStarted("dotnet", ".NET SDK"),
			MapLine("""{"type":"checkup_started","id":"dotnet","name":".NET SDK"}"""));

	[Fact]
	[Description("An empty progress message would blank a row's detail line, so it is dropped rather than applied.")]
	public void Progress_WithoutMessage_IsIgnored()
		=> Assert.Null(MapLine("""{"type":"checkup_progress","id":"dotnet","message":""}"""));

	[Fact]
	[Description("Unknown event types are ignored so a newer CLI emitting extra events cannot break an older host.")]
	public void UnknownEventType_IsIgnored()
		=> Assert.Null(MapLine("""{"type":"something_new","id":"x"}"""));

	[Fact]
	[Description("Non-JSON stdout (a stray write that escaped the contract's stdout purity) must not throw into the read loop.")]
	public void MalformedLine_ParsesToNull()
		=> Assert.Null(UnoCheckProtocol.TryParseLine("not json at all"));

	[Fact]
	[Description("Catalog titles come from schema 1.0's name field; falling back to the id would show raw ids like 'vswinworkloads' in the rail.")]
	public void Catalog_PrefersNameAsTitle()
	{
		var parsed = UnoCheckProtocol.TryParseLine("""
			{"type":"checkup_catalog","schema_version":"1.0","checkups":[{"id":"openjdk","name":"OpenJDK 17.0.16","type_name":"OpenJdkCheckup"}]}
			""");

		var entries = UnoCheckProtocol.MapCatalog(parsed!);

		Assert.Equal(new UnoCheckCatalogEntry("openjdk", "OpenJDK 17.0.16"), Assert.Single(entries));
	}

	[Fact]
	[Description("An engine predating the name/title rename still yields readable titles rather than ids.")]
	public void Catalog_FallsBackToTitleThenId()
	{
		var parsed = UnoCheckProtocol.TryParseLine("""
			{"type":"checkup_catalog","checkups":[{"id":"vswin","title":"Visual Studio 18.0"},{"id":"bare"}]}
			""");

		var entries = UnoCheckProtocol.MapCatalog(parsed!);

		Assert.Equal("Visual Studio 18.0", entries[0].Title);
		Assert.Equal("bare", entries[1].Title);
	}

	[Fact]
	[Description("A non-catalog line yields no entries, so an event stream can never be mistaken for a catalog.")]
	public void Catalog_IgnoresNonCatalogLines()
	{
		var parsed = UnoCheckProtocol.TryParseLine("""{"type":"run_started","checkup_count":3}""");

		Assert.Empty(UnoCheckProtocol.MapCatalog(parsed!));
	}
}
