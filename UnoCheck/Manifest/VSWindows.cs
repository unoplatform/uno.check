using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck.Manifest
{
	public partial class VSWindows
	{
		[JsonProperty("workloads")]
		public List<VSWinWorkload> Workloads { get; set; }
	}
}
