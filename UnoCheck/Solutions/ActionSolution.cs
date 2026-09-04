using DotNetCheck.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class ActionSolution : Solution
	{
		/// <param name="requiresElevation">
		/// Whether the action needs administrator/root rights. Left at <see langword="true"/>
		/// unless the call site knows the action only writes user-scoped state — see
		/// <see cref="Solution.RequiresElevation"/>.
		/// </param>
		public ActionSolution(Func<Solution, CancellationToken, Task> curer, bool requiresElevation = true)
		{
			Curer = curer;
			RequiresElevation = requiresElevation;
		}

		public Func<Solution, CancellationToken, Task> Curer { get; private set; }

		public override bool RequiresElevation { get; }

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			await Curer?.Invoke(this, cancellationToken);
		}
	}
}
