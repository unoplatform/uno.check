using DotNetCheck.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCheck
{
	public class ListCheckupCommand : AsyncCommand<ListCheckupSettings>
	{
		public override async Task<int> ExecuteAsync(CommandContext context, ListCheckupSettings settings)
		{
			System.IO.TextWriter jsonOut = null;

			if (settings.Json)
			{
				// Same stdout discipline as the check command: the JSON line owns the real
				// stdout; everything human-readable moves to stderr.
				settings.NonInteractive = true;
				jsonOut = Console.Out;
				Console.SetOut(Console.Error);
				AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
				{
					Ansi = AnsiSupport.Detect,
					Out = new AnsiConsoleOutput(Console.Error),
				});
			}

			var manifest = await ToolInfo.LoadManifest(settings.Manifest, settings.GetManifestChannel());

			if (!ToolInfo.Validate(manifest))
			{
				ToolInfo.ExitPrompt(settings.NonInteractive);
				return -1;
			}

			var sharedState = new SharedState();

			var checkups = CheckupManager.BuildCheckupGraph(manifest, sharedState, settings.TargetPlatforms);

			if (jsonOut is not null)
			{
				var items = new List<Json.CheckupCatalogItem>();

				foreach (var c in checkups)
				{
					// Titles can derive from manifest values; set it the same way the
					// check loop does, and fall back to the type name if a checkup's
					// title cannot resolve outside a real run.
					c.Manifest = manifest;

					string title;
					try { title = c.Title; }
					catch { title = c.GetType().Name; }

					items.Add(new Json.CheckupCatalogItem
					{
						Id = c.Id,
						Name = c.GetType().Name,
						Title = title,
					});
				}

				jsonOut.WriteLine(Json.JsonlOutput.Render(new Json.CheckupCatalogEvent { Checkups = items }));
				jsonOut.Flush();
				return 0;
			}

			foreach (var c in checkups)
			{
				AnsiConsole.WriteLine(c.GetType().Name.ToString() + " (" + c.Id + ")");
			}

			ToolInfo.ExitPrompt(settings.NonInteractive);
			return 0;
		}

	}
}
