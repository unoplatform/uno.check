using System;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetCheck.Models
{
	public abstract class Solution
	{
		public Solution()
		{ }

		public virtual Task Implement(SharedState sharedState, System.Threading.CancellationToken cancellationToken)
			=> Task.CompletedTask;

		/// <summary>
		/// Whether applying this solution needs administrator/root rights. Surfaced to
		/// structured hosts as <c>fix.requires_elevation</c> so a host can decide, before
		/// launching, whether the fix child needs elevation — Windows has no per-command
		/// elevation, so without this signal a host must elevate every fix.
		///
		/// Defaults to <see langword="true"/>: an unclassified solution keeps today's
		/// conservative behavior (a needless prompt is an annoyance; a missing one is a
		/// failed fix). Solutions that only ever write user-scoped state override it to
		/// <see langword="false"/>, and solutions whose target depends on the machine layout
		/// compute it from that target's writability.
		/// </summary>
		public virtual bool RequiresElevation => true;

		public void ReportStatus(string message)
			=> OnStatusUpdated?.Invoke(this, new RemedyStatusEventArgs(this, message));

		public event EventHandler<RemedyStatusEventArgs> OnStatusUpdated;
	}
}
