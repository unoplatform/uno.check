using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck.Models
{
	partial class Checkup
	{
		/// <summary>
		/// Target platforms to which the checkup applies.
		/// </summary>
		public virtual TargetPlatform GetApplicableTargets(Manifest.Manifest manifest) => TargetPlatform.All;
	}
}
