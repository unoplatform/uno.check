using System.Diagnostics;
using System.Text.Json;

namespace UnoCheck.Gui.Services;

/// <summary>
/// Drives the uno-check CLI through its structured-output contract:
/// JSONL events on stdout for unelevated runs, and a tailed --json-file
/// for elevated fix runs (an elevated child's stdout cannot be redirected
/// across the elevation boundary on Windows).
/// </summary>
public sealed class UnoCheckClient
{
	/// <summary>
	/// Version used on the dnx path. dnx runs the shipped tool package through the dotnet
	/// host, where the exe's requireAdministrator manifest does not apply — verified: the
	/// released tool starts unelevated this way on Windows. Bump to the first release that
	/// contains the --json contract; until then the locally built CLI is preferred.
	/// </summary>
	const string PinnedToolVersion = "1.34.1";

	readonly string? _localCliDll = TryFindLocalCli();

	/// <summary>Human-readable description of the engine this client resolved to.</summary>
	public string EngineDescription => _localCliDll is not null
		? "locally built UnoCheck.dll"
		: $"dnx uno.check@{PinnedToolVersion}";

	/// <summary>Prefers the locally built UnoCheck.dll (repo checkout, has the --json contract).</summary>
	static string? TryFindLocalCli()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "UnoCheck", "bin", "Debug", "net6.0", "UnoCheck.dll");
			if (File.Exists(candidate))
				return candidate;
			dir = dir.Parent;
		}
		return null;
	}

	// dnx ships as dnx.ps1 forwarding to `dotnet dnx`, so invoke the muxer directly.
	// --yes auto-confirms the one-time package download (cached afterwards).
	(string fileName, string argPrefix) GetInvocation()
		=> _localCliDll is not null
			? ("dotnet", $"\"{_localCliDll}\" ")
			: ("dotnet", $"dnx uno.check@{PinnedToolVersion} --yes -- ");

	/// <summary>
	/// Fetches the checkup catalog (`list --json`): the checkups applicable to this machine,
	/// for building the selection UI. Returns an empty list if the engine predates the flag.
	/// </summary>
	public async Task<IReadOnlyList<(string Id, string Title)>> GetCatalogAsync(CancellationToken ct = default)
	{
		var items = new List<(string, string)>();

		await RunAsync("list", evt =>
		{
			if (evt.TryGetProperty("type", out var t) && t.GetString() == "checkup_catalog"
				&& evt.TryGetProperty("checkups", out var checkups))
			{
				foreach (var c in checkups.EnumerateArray())
				{
					var id = c.TryGetProperty("id", out var i) ? i.GetString() : null;
					var title = c.TryGetProperty("title", out var ti) ? ti.GetString() : id;
					if (!string.IsNullOrEmpty(id))
						items.Add((id!, title ?? id!));
				}
			}
		}, ct);

		return items;
	}

	/// <summary>Unelevated run streaming JSONL from stdout. Used for diagnosis (and fixes when already admin).</summary>
	public async Task<int> RunAsync(string args, Action<JsonElement> onEvent, CancellationToken ct = default)
	{
		var (fileName, argPrefix) = GetInvocation();

		var psi = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = $"{argPrefix}{args} --non-interactive --json",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};

		using var process = new Process { StartInfo = psi };

		process.OutputDataReceived += (_, e) =>
		{
			if (string.IsNullOrWhiteSpace(e.Data))
				return;
			try
			{
				using var doc = JsonDocument.Parse(e.Data);
				onEvent(doc.RootElement.Clone());
			}
			catch (JsonException)
			{
				// Not an event line; ignore.
			}
		};
		process.ErrorDataReceived += (_, _) => { };

		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		try
		{
			await process.WaitForExitAsync(ct);
		}
		catch (OperationCanceledException)
		{
			try { process.Kill(entireProcessTree: true); } catch { }
			throw;
		}

		return process.ExitCode;
	}

	/// <summary>
	/// Elevated fix run: launches the CLI via the shell "runas" verb (UAC prompt) with
	/// --json-file, and tails that file for events until the child exits.
	/// </summary>
	public async Task<int> RunFixElevatedAsync(string checkupId, string extraArgs, Action<JsonElement> onEvent, CancellationToken ct = default)
	{
		var (fileName, argPrefix) = GetInvocation();
		var eventsFile = Path.Combine(Path.GetTempPath(), $"unocheck-fix-{Guid.NewGuid():n}.jsonl");

		var args = $"{argPrefix}--fix --only {checkupId} {extraArgs}".Trim();
		var psi = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = $"{args} --non-interactive --json-file \"{eventsFile}\"",
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Hidden,
		};

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException("Failed to start elevated uno-check.");

		long offset = 0;
		while (!process.HasExited)
		{
			offset = DrainEvents(eventsFile, offset, onEvent);
			await Task.Delay(300, ct);
		}
		DrainEvents(eventsFile, offset, onEvent);

		try { File.Delete(eventsFile); } catch { }

		return process.ExitCode;
	}

	static long DrainEvents(string path, long offset, Action<JsonElement> onEvent)
	{
		if (!File.Exists(path))
			return offset;

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (stream.Length <= offset)
			return offset;

		stream.Seek(offset, SeekOrigin.Begin);
		using var reader = new StreamReader(stream);

		string? line;
		while ((line = reader.ReadLine()) is not null)
		{
			if (string.IsNullOrWhiteSpace(line))
				continue;
			try
			{
				using var doc = JsonDocument.Parse(line);
				onEvent(doc.RootElement.Clone());
			}
			catch (JsonException)
			{
				// Partial trailing line; re-read from here next drain.
				return stream.Position - System.Text.Encoding.UTF8.GetByteCount(line + Environment.NewLine);
			}
		}

		return stream.Position;
	}

	public static bool IsElevated()
	{
		if (!OperatingSystem.IsWindows())
			return false;
		using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
		return new System.Security.Principal.WindowsPrincipal(identity)
			.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
	}
}
