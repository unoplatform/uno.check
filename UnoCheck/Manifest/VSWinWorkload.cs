#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck.Manifest
{
	public partial class VSWinWorkload
	{
		[JsonProperty("id")]
		public string? Id { get; set; }

		[JsonProperty("name")]
		public string? Name { get; set; }

		[JsonProperty("requiredby")]
		public List<string>? RequiredBy { get; set; }
	}
}
