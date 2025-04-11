﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	public partial class Manifest
	{
<<<<<<< HEAD
		public const string DefaultManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/0b3e526824910bfedccafa7359ac5151bcaf1823/manifests/uno.ui.manifest.json";
		public const string PreviewManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/0b3e526824910bfedccafa7359ac5151bcaf1823/manifests/uno.ui-preview.manifest.json";
		public const string PreviewMajorManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/0b3e526824910bfedccafa7359ac5151bcaf1823/manifests/uno.ui-preview-major.manifest.json";
=======
		public const string DefaultManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/e02af3e5fb82b180725ff20bf51598406d859b85/manifests/uno.ui.manifest.json";
		public const string PreviewManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/e02af3e5fb82b180725ff20bf51598406d859b85/manifests/uno.ui-preview.manifest.json";
		public const string PreviewMajorManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/e02af3e5fb82b180725ff20bf51598406d859b85/manifests/uno.ui-preview-major.manifest.json";
>>>>>>> 1e7b03b (chore: Bump default manifest)

		public static Task<Manifest> FromFileOrUrl(string fileOrUrl)
		{
			if (fileOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				return FromUrl(fileOrUrl);

			return FromFile(fileOrUrl);
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
