#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Reporting;

internal static class CheckReportWriter
{
    private const string ReportFileName = "report.json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string GetDefaultReportPath()
	{
		var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(basePath))
			throw new InvalidOperationException("Local application data folder could not be resolved.");

		var reportDirectory = Path.Combine(basePath, "Uno Platform", "uno-check");

		return Path.Combine(reportDirectory, ReportFileName);
	}

    public static Task WriteReportAsync(CheckReport report, CancellationToken cancellationToken = default)
    {
        var path = GetDefaultReportPath();
        return WriteReportAsync(report, path, cancellationToken);
    }

    public static async Task WriteReportAsync(
        CheckReport report,
        string reportPath,
        CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(reportPath))
        {
            throw new ArgumentException("Report path must be provided.", nameof(reportPath));
        }

        var reportDirectory = Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(reportDirectory))
        {
            throw new InvalidOperationException($"Unable to determine directory for report path '{reportPath}'.");
        }

        Directory.CreateDirectory(reportDirectory);

        var payload = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(reportPath, payload, cancellationToken).ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }
}
