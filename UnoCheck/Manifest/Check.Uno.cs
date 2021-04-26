#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck.Manifest
{
	partial class Check
	{
		[JsonProperty("vswindows")]
		public VSWindows? VSWindows { get; set; }
	}
}
