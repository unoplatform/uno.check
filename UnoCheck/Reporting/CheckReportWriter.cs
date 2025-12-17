#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Reporting;

internal static class CheckReportWriter
{
	private const string ReportFilePrefix = "report-";
	private const string ReportFileExtension = ".json";
	private const int MaxReportFiles = 50;

	private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

	private static string GetDefaultReportPath(CheckReport report)
	{
		if (report is null)
			throw new ArgumentNullException(nameof(report));

		var reportDirectory = GetDefaultReportDirectory();
		var fileName = CreateReportFileName(report.StartedAtUtc);

		return Path.Combine(reportDirectory, fileName);
	}

	private static string GetDefaultReportDirectory()
	{
		var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(basePath))
			throw new InvalidOperationException("Local application data folder could not be resolved.");

		return Path.Combine(basePath, "Uno Platform", "uno-check", "reports");
	}

	private static string CreateReportFileName(DateTimeOffset startedAtUtc)
	{
		var timestamp = startedAtUtc.ToString("yyyyMMddTHHmmssfffffffZ");
		return $"{ReportFilePrefix}{timestamp}-{Guid.NewGuid():N}{ReportFileExtension}";
	}

	public static Task WriteReportAsync(CheckReport report, CancellationToken cancellationToken = default)
	{
		var path = GetDefaultReportPath(report);
		return WriteReportAsync(report, path, enableCleanup: true, cancellationToken);
	}

	public static Task WriteReportAsync(
		CheckReport report,
		string reportPath,
		CancellationToken cancellationToken = default)
	{
		var reportDirectory = ValidateReportInputs(report, reportPath);

		return WriteReportAsync(
			report,
			reportPath,
			IsDefaultReportDirectory(reportDirectory),
			reportDirectory,
			cancellationToken);
	}

	internal static Task WriteReportAsync(
		CheckReport report,
		string reportPath,
		bool enableCleanup,
		CancellationToken cancellationToken = default)
	{
		var reportDirectory = ValidateReportInputs(report, reportPath);
		return WriteReportAsync(report, reportPath, enableCleanup, reportDirectory, cancellationToken);
	}

	private static async Task WriteReportAsync(
		CheckReport report,
		string reportPath,
		bool enableCleanup,
		string reportDirectory,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(reportDirectory);

		var payload = JsonSerializer.Serialize(report, SerializerOptions);
		var tempPath = Path.Combine(
			reportDirectory,
			$"{Path.GetFileNameWithoutExtension(reportPath)}.{Guid.NewGuid():N}.tmp");

		await File.WriteAllTextAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);

		if (File.Exists(reportPath))
		{
			File.Delete(reportPath);
		}

		File.Move(tempPath, reportPath);

		if (enableCleanup)
		{
			TrimReportDirectory(reportDirectory);
		}
	}

	private static void TrimReportDirectory(string reportDirectory)
	{
		try
		{
			var files = new DirectoryInfo(reportDirectory)
				.EnumerateFiles($"{ReportFilePrefix}*{ReportFileExtension}", SearchOption.TopDirectoryOnly)
				.OrderByDescending(f => f.LastWriteTimeUtc)
				.ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
				.Skip(MaxReportFiles)
				.ToArray();

			foreach (var file in files)
			{
				try
				{
					file.Delete();
				}
				catch
				{
					// Best-effort cleanup; ignore issues.
				}
			}
		}
		catch
		{
			// Cleanup should not block report writing.
		}
	}

	private static string ValidateReportInputs(CheckReport report, string reportPath)
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

		return reportDirectory;
	}

	private static bool IsDefaultReportDirectory(string reportDirectory)
	{
		try
		{
			var defaultDirectory = GetDefaultReportDirectory();
			return string.Equals(
				Path.GetFullPath(reportDirectory),
				Path.GetFullPath(defaultDirectory),
				StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
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
