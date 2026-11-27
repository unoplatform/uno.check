namespace DotNetCheck.Models
{
	public class DiagnosticResult
	{
		public static DiagnosticResult Ok(Checkup checkup)
			=> new DiagnosticResult(Status.Ok, checkup);

		public DiagnosticResult(Status status, Checkup checkup, string message)
			: this(status, checkup, message, null)
		{
		}

		public DiagnosticResult(Status status, Checkup checkup, Suggestion prescription)
			: this(status, checkup, null, prescription)
		{
		}

		public DiagnosticResult(Status status, Checkup checkup, string message = null, Suggestion prescription = null)
		{
			Status = status;
			Checkup = checkup;
			Message = message;
			Suggestion = prescription;
		}

		/// <summary>A check that could not look, for example because access was refused: a warning with no fix.</summary>
		/// <param name="checkup">The check.</param>
		/// <param name="message">What stopped it.</param>
		/// <returns>The result, marked as not checked.</returns>
		internal static DiagnosticResult NotChecked(Checkup checkup, string message)
			=> new DiagnosticResult(Status.Warning, checkup, message) { CouldNotCheck = true };

		/// <summary>Whether the check could not look, so its status says nothing about what it checks.</summary>
		public bool CouldNotCheck { get; private set; }

		public Status Status { get; private set; }
		public Checkup Checkup { get; private set; }

		public virtual string Message { get; private set; }

		public bool HasSuggestion => Suggestion != null;

		public virtual Suggestion Suggestion { get; private set; }
	}
}
