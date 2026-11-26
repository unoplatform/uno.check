using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;

namespace DotNetCheck.Models
{
	/// <summary>
	/// A check that was refused access can't tell "not installed" from "couldn't look". It reports that it could
	/// not check, as a warning with no fix, instead of reporting the tool missing (issue #647).
	/// </summary>
	internal static class AccessRefused
	{
		internal const string MessagePrefix = "Could not check: ";

		const int ErrorAccessDenied = 5;
		const string ExceptionName = nameof(UnauthorizedAccessException);
		const string DeniedPathStart = "Access to the path";
		const string DeniedPathEnd = "denied";

		/// <summary>The verdict of a check that could not look.</summary>
		/// <param name="checkup">The check.</param>
		/// <param name="what">What was refused, naming the path where it is known.</param>
		/// <returns>A warning with no suggestion.</returns>
		internal static DiagnosticResult Result(Checkup checkup, string what)
			=> DiagnosticResult.NotChecked(checkup, MessagePrefix + what);

		internal static bool IsAccessRefused(Exception exception)
			=> FindRefusal(exception) is not null;

		/// <summary>The exception in <paramref name="exception"/>'s chain that says access was refused.</summary>
		/// <param name="exception">The failure to look into.</param>
		/// <returns>That exception, or null when the failure is something else.</returns>
		internal static Exception FindRefusal(Exception exception)
		{
			if (exception is null)
				return null;

			if (exception is UnauthorizedAccessException or SecurityException
				|| exception is Win32Exception { NativeErrorCode: ErrorAccessDenied })
			{
				return exception;
			}

			if (exception is AggregateException aggregate)
				return aggregate.InnerExceptions.Select(FindRefusal).FirstOrDefault(found => found is not null);

			return FindRefusal(exception.InnerException);
		}

		/// <summary>The line of a child process's output that reports a refused access.</summary>
		/// <param name="lines">The process's output and error lines.</param>
		/// <returns>That line, or null when none does.</returns>
		internal static string FindInOutput(IEnumerable<string> lines)
			=> (lines ?? Enumerable.Empty<string>()).FirstOrDefault(line =>
				line is not null
				&& (line.Contains(ExceptionName, StringComparison.Ordinal)
					|| (line.Contains(DeniedPathStart, StringComparison.OrdinalIgnoreCase) && line.Contains(DeniedPathEnd, StringComparison.OrdinalIgnoreCase))));

		/// <summary>Whether listing <paramref name="folder"/> is refused, as opposed to the folder being absent or readable.</summary>
		/// <param name="folder">The folder to list.</param>
		/// <param name="enumerate">How to list it; the file system by default.</param>
		/// <returns>True only when access is refused.</returns>
		internal static bool IsReadRefused(string folder, Func<string, IEnumerable<string>> enumerate = null)
		{
			try
			{
				_ = (enumerate ?? Directory.EnumerateFileSystemEntries)(folder).FirstOrDefault();
				return false;
			}
			catch (Exception ex) when (IsAccessRefused(ex))
			{
				Util.Exception(ex);
				return true;
			}
			catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
			{
				Util.Exception(ex);
				return false;
			}
		}
	}
}
