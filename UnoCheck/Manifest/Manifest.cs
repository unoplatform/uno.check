using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	public partial class Manifest
	{
		public const string DefaultManifestResourceName = "uno.ui.manifest.json";
		public const string PreviewManifestResourceName = "uno.ui-preview.manifest.json";
		public const string PreviewMajorManifestResourceName = "uno.ui-preview-major.manifest.json";

		public static Task<Manifest> FromFileOrUrl(string fileOrUrl)
		{
			if (fileOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				return FromUrl(fileOrUrl);

			return FromFile(fileOrUrl);
		}

		public static async Task<Manifest> FromEmbeddedResource(string resourceName)
		{
			var assembly = typeof(Manifest).Assembly;
			using var stream = assembly.GetManifestResourceStream(resourceName);
			
			if (stream == null)
				throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

			using var reader = new StreamReader(stream);
			var json = await reader.ReadToEndAsync();
			return await FromJson(json);
		}

		public static async Task<Manifest> FromFile(string filename)
		{
			var json = await System.IO.File.ReadAllTextAsync(filename);
			return await FromJson(json);
		}

		public static async Task<Manifest> FromUrl(string url)
		{
			var http = new HttpClient();
			var json = await http.GetStringAsync(url);

			return await FromJson(json);
		}

		public static async Task<Manifest> FromJson(string json)
		{
			var m = JsonConvert.DeserializeObject<Manifest>(json, new JsonSerializerSettings {
				TypeNameHandling = TypeNameHandling.Auto
			});

			await m?.Check?.MapVariables();

			return m;
		}

		[JsonProperty("check")]
		public Check Check { get; set; }
	}
}
