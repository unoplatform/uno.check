using Newtonsoft.Json;

namespace DotNetCheck.Manifest
{
	public partial class Gtk
	{
		[JsonProperty("urls")]
		public Urls Urls { get; set; }

		[JsonIgnore]
		public System.Uri Url
			=> Urls?.Get(MinimumVersion);

		[JsonProperty("version")]
		public string Version { get; set; }

		[JsonProperty("minimumVersion")]
		public string MinimumVersion { get; set; } = "3.24.31";
	}
}