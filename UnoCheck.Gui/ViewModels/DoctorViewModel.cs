using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using UnoCheck.Gui.Services;

namespace UnoCheck.Gui.ViewModels;

public partial class DoctorViewModel : ObservableObject
{
	readonly UnoCheckClient _client = new();
	readonly DispatcherQueue _dispatcher;
	CancellationTokenSource? _cts;

	public DoctorViewModel(DispatcherQueue dispatcher)
	{
		_dispatcher = dispatcher;

		Targets = new ObservableCollection<TargetOption>(BuildDefaultTargets());
		LoadSettings();
	}

	public ObservableCollection<CheckCardViewModel> Cards { get; } = new();

	// ── Run options (mirrors the Mountaineer Doctor screen) ──────────

	public ObservableCollection<TargetOption> Targets { get; }

	public string[] Channels { get; } = ["Stable", "Preview", "Preview major", "Main"];

	[ObservableProperty]
	private int _selectedChannelIndex;

	[ObservableProperty]
	private bool _verbose;

	static IEnumerable<TargetOption> BuildDefaultTargets()
	{
		// Defaults suit the current OS: no Apple-only targets preselected on Windows, etc.
		var isWindows = OperatingSystem.IsWindows();
		var isMac = OperatingSystem.IsMacOS();

		yield return new TargetOption("WASM", "wasm", true);
		yield return new TargetOption("Android", "android", true);
		yield return new TargetOption("iOS", "ios", isMac);
		yield return new TargetOption("Skia/Desktop", "skia", true);
		yield return new TargetOption("Windows", "windows", isWindows);
		yield return new TargetOption("macOS", "macos", isMac);
	}

	string BuildRunArgs()
	{
		var args = string.Join(" ", Targets.Where(t => t.IsChecked).Select(t => $"--target {t.Flag}"));

		args += SelectedChannelIndex switch
		{
			1 => " --pre",
			2 => " --pre-major",
			3 => " --main",
			_ => "",
		};

		if (Verbose)
			args += " -v";

		return args.Trim();
	}

	// ── Settings persistence (spec 084: no re-picking every visit) ───

	static string SettingsPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"UnoCheck.Gui", "settings.json");

	void LoadSettings()
	{
		try
		{
			if (!File.Exists(SettingsPath))
				return;

			var root = JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement;

			if (root.TryGetProperty("targets", out var targets))
			{
				var picked = targets.EnumerateArray().Select(t => t.GetString()).ToHashSet();
				foreach (var t in Targets)
					t.IsChecked = picked.Contains(t.Flag);
			}
			if (root.TryGetProperty("channel", out var ch))
				SelectedChannelIndex = Math.Clamp(ch.GetInt32(), 0, Channels.Length - 1);
			if (root.TryGetProperty("verbose", out var v))
				Verbose = v.GetBoolean();
		}
		catch
		{
			// Corrupt/unreadable settings: fall back to defaults.
		}
	}

	void SaveSettings()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
			File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new
			{
				targets = Targets.Where(t => t.IsChecked).Select(t => t.Flag),
				channel = SelectedChannelIndex,
				verbose = Verbose,
			}));
		}
		catch
		{
			// Persistence is a convenience; never fail a run over it.
		}
	}

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(RunCommand))]
	[NotifyCanExecuteChangedFor(nameof(CancelCommand))]
	private bool _isRunning;

	[ObservableProperty]
	private string _summary = "Ready — run a check to diagnose this machine.";

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task RunAsync()
	{
		IsRunning = true;
		Cards.Clear();
		Summary = "Running uno-check…";
		SaveSettings();
		_cts = new CancellationTokenSource();

		try
		{
			var exit = await Task.Run(() => _client.RunAsync(BuildRunArgs(), OnEvent, _cts.Token));
			if (Summary.StartsWith("Running"))
				Summary = $"Run finished (exit {exit}).";
		}
		catch (OperationCanceledException)
		{
			Summary = "Canceled.";
		}
		catch (Exception ex)
		{
			Summary = $"Failed to run uno-check: {ex.Message}";
		}
		finally
		{
			IsRunning = false;
			_cts = null;
		}
	}

	private bool CanRun() => !IsRunning;

	[RelayCommand(CanExecute = nameof(IsRunning))]
	private void Cancel() => _cts?.Cancel();

	/// <summary>Set by the view; ContentDialogs need a live XamlRoot.</summary>
	public XamlRoot? XamlRoot { get; set; }

	public async Task FixAsync(CheckCardViewModel card)
	{
		card.IsBusy = true;

		// Progress popup: fix events stream into it live (tailed from --json-file when
		// the child runs elevated — its stdout can't cross the UAC boundary).
		var log = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "Starting fix…" };
		var progress = new ProgressBar { IsIndeterminate = true };
		var dialog = new ContentDialog
		{
			Title = $"Fixing: {card.Name}",
			Content = new StackPanel
			{
				Spacing = 8,
				MinWidth = 420,
				Children = { progress, new ScrollViewer { Content = log, MaxHeight = 320 } },
			},
			PrimaryButtonText = "Close",
			IsPrimaryButtonEnabled = false,
			XamlRoot = XamlRoot,
		};

		void AppendLine(string line)
			=> _dispatcher.TryEnqueue(() => log.Text += Environment.NewLine + line);

		var fixSucceeded = false;

		void OnFixEvent(JsonElement evt)
		{
			OnEvent(evt);

			var type = evt.TryGetProperty("type", out var t) ? t.GetString() : null;
			switch (type)
			{
				case "fix_started":
					AppendLine($"Applying {evt.GetProperty("solution").GetString()}…");
					break;
				case "fix_progress" or "checkup_progress":
					if (evt.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } msg)
						AppendLine(msg);
					break;
				case "fix_result":
					fixSucceeded = evt.GetProperty("success").GetBoolean();
					AppendLine(fixSucceeded
						? "Fix applied — verifying…"
						: evt.TryGetProperty("error", out var err) ? $"Fix failed: {err.GetString()}" : "Fix failed.");
					break;
			}
		}

		var showTask = dialog.ShowAsync();
		var failed = false;

		try
		{
			// Fixes may write to machine-scoped locations; elevate the child on Windows
			// unless this process is already admin. No separate re-check needed:
			// uno-check re-examines the checkup itself after a successful fix, and that
			// checkup_result flows through the same event stream to update the card.
			// The fix must run against the same manifest channel as the diagnosis.
			var channelArg = SelectedChannelIndex switch { 1 => "--pre", 2 => "--pre-major", 3 => "--main", _ => "" };

			var elevated = OperatingSystem.IsWindows() && !UnoCheckClient.IsElevated();
			_ = elevated
				? await Task.Run(() => _client.RunFixElevatedAsync(card.Id, channelArg, OnFixEvent))
				: await Task.Run(() => _client.RunAsync($"--fix --only {card.Id} {channelArg}".Trim(), OnFixEvent));

			failed = !fixSucceeded;
		}
		catch (Exception ex)
		{
			// Includes the user declining the UAC prompt (Win32 error 1223).
			failed = true;
			AppendLine($"Fix did not run: {ex.Message}");
		}
		finally
		{
			card.IsBusy = false;

			var close = !failed;
			_dispatcher.TryEnqueue(async () =>
			{
				progress.IsIndeterminate = false;
				progress.Value = 100;

				if (close)
				{
					// Success: let the final state register, then get out of the way.
					await Task.Delay(600);
					dialog.Hide();
				}
				else
				{
					// Failure: keep the log on screen until the user closes it.
					dialog.IsPrimaryButtonEnabled = true;
				}
			});
		}

		await showTask;
	}

	void OnEvent(JsonElement evt)
	{
		var type = evt.TryGetProperty("type", out var t) ? t.GetString() : null;
		if (type is null)
			return;

		_dispatcher.TryEnqueue(() => Apply(type, evt));
	}

	void Apply(string type, JsonElement evt)
	{
		switch (type)
		{
			case "checkup_started":
			{
				var id = evt.GetProperty("id").GetString() ?? "";
				var card = FindOrAddCard(id);
				card.Name = evt.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
				card.SetStatus("running");
				break;
			}
			case "checkup_progress":
			{
				if (evt.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id
					&& evt.TryGetProperty("message", out var m))
				{
					FindOrAddCard(id).Detail = m.GetString();
				}
				break;
			}
			case "checkup_result":
			{
				var check = evt.GetProperty("check");
				var id = check.GetProperty("id").GetString() ?? "";
				var card = FindOrAddCard(id);
				card.Name = check.TryGetProperty("name", out var n) ? n.GetString() ?? id : id;
				card.Message = check.TryGetProperty("message", out var msg) ? msg.GetString() : null;
				if (check.TryGetProperty("skip_reason", out var skip))
					card.Message = skip.GetString();
				card.CanFix = check.TryGetProperty("fix", out var fix)
					&& fix.TryGetProperty("auto_fixable", out var af) && af.GetBoolean();
				card.Detail = null; // final state: drop stale progress text
				card.SetStatus(check.GetProperty("status").GetString() ?? "error");
				break;
			}
			case "fix_progress":
			{
				if (evt.TryGetProperty("id", out var idProp) && idProp.GetString() is { } id
					&& evt.TryGetProperty("message", out var m))
				{
					FindOrAddCard(id).Detail = m.GetString();
				}
				break;
			}
			case "fix_result":
			{
				var id = evt.GetProperty("id").GetString() ?? "";
				var card = FindOrAddCard(id);
				var success = evt.GetProperty("success").GetBoolean();
				card.Detail = success
					? "Fix applied — re-checking…"
					: (evt.TryGetProperty("error", out var err) ? $"Fix failed: {err.GetString()}" : "Fix failed.");
				break;
			}
			case "report":
			{
				var summary = evt.GetProperty("report").GetProperty("summary");
				Summary = $"Done — {summary.GetProperty("ok").GetInt32()} ok, " +
					$"{summary.GetProperty("warning").GetInt32()} warnings, " +
					$"{summary.GetProperty("error").GetInt32()} errors, " +
					$"{summary.GetProperty("skipped").GetInt32()} skipped.";
				break;
			}
		}
	}

	CheckCardViewModel FindOrAddCard(string id)
	{
		var card = Cards.FirstOrDefault(c => c.Id == id);
		if (card is null)
		{
			card = new CheckCardViewModel(this) { Id = id, Name = id };
			Cards.Add(card);
		}
		return card;
	}
}

public partial class TargetOption : ObservableObject
{
	public TargetOption(string label, string flag, bool isChecked)
	{
		Label = label;
		Flag = flag;
		_isChecked = isChecked;
	}

	public string Label { get; }
	public string Flag { get; }

	[ObservableProperty]
	private bool _isChecked;
}

public partial class CheckCardViewModel : ObservableObject
{
	readonly DoctorViewModel _owner;

	public CheckCardViewModel(DoctorViewModel owner)
	{
		_owner = owner;
	}

	public string Id { get; init; } = "";

	[ObservableProperty]
	private string _name = "";

	[ObservableProperty]
	private string? _message;

	[ObservableProperty]
	private string? _detail;

	[ObservableProperty]
	private bool _canFix;

	[ObservableProperty]
	private bool _isBusy;

	[ObservableProperty]
	private string _statusGlyph = "○";

	[ObservableProperty]
	private Brush _statusBrush = new SolidColorBrush(Colors.Gray);

	public void SetStatus(string status)
	{
		(StatusGlyph, StatusBrush) = status switch
		{
			"ok" => ("✔", (Brush)new SolidColorBrush(Colors.Green)),
			"warning" => ("!", new SolidColorBrush(Colors.DarkOrange)),
			"error" => ("✘", new SolidColorBrush(Colors.Red)),
			"skipped" => ("↷", new SolidColorBrush(Colors.Gray)),
			"running" => ("…", new SolidColorBrush(Colors.DodgerBlue)),
			_ => ("○", new SolidColorBrush(Colors.Gray)),
		};

		if (status is "ok" or "skipped")
			CanFix = false;
	}

	[RelayCommand]
	private Task FixAsync() => _owner.FixAsync(this);
}
