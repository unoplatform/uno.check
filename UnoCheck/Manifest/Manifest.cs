using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	public partial class Manifest
	{
		public const string DefaultManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/209d531333ecc43eeec048e60fb62f679624b0e6/manifests/uno.ui.manifest.json";
		public const string PreviewManifestUrl = "https://raw.githubusercontent.com/unoplatform/uno.check/938d5f4610404f240ec5de18e366cc84cff81c4b/manifests/uno.ui-preview.manifest.json";

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
