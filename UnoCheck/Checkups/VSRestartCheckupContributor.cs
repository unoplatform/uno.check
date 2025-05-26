using DotNetCheck.Models;
using System.Collections.Generic;

namespace DotNetCheck.Checkups
{
    public class VSRestartCheckupContributor : CheckupContributor
    {
        public override IEnumerable<Checkup> Contribute(Manifest.Manifest manifest, SharedState sharedState)
        {
            // Only contribute if we're on Windows
            if (Util.IsWindows)
            {
                yield return new VSRestartCheckup();
            }
        }
    }
} 