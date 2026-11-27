using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace DotNetCheck.Models
{
	/// <summary>
	/// A check that was refused access can't tell "not installed" from "couldn't look". It reports that it could
	/// not check, as a warning with no fix, instead of reporting the tool missing.
	/// </summary>
	internal static class AccessRefused
	{
		internal const string MessagePrefix = "Could not check: ";
		internal const int MaxDetailLength = 300;

		const int WindowsAccessDenied = 5;
		const int UnixAccessDenied = 13;
		const string ExceptionName = nameof(UnauthorizedAccessException);
		const string SecurityExceptionName = nameof(SecurityException);
		const string DeniedPathStart = "Access to the path";
		const string DeniedPathEnd = "denied";
		const string DeniedRegistry = "Requested registry access is not allowed";

		/// <summary>The verdict of a check that could not look.</summary>
		/// <param name="checkup">The check.</param>
		/// <param name="what">What was refused, naming the path where it is known.</param>
		/// <returns>A warning with no suggestion, marked as not checked.</returns>
		internal static DiagnosticResult Result(Checkup checkup, string what)
			=> DiagnosticResult.NotChecked(checkup, MessagePrefix + Summarize(what));

		/// <summary>The verdict of a check that was refused a folder it has to read.</summary>
		/// <param name="checkup">The check.</param>
		/// <param name="folder">The folder that could not be read.</param>
		/// <param name="consequence">What can't be known without it.</param>
		/// <returns>A warning with no suggestion, marked as not checked.</returns>
		internal static DiagnosticResult FolderResult(Checkup checkup, string folder, string consequence)
			=> Result(checkup, $"access to '{folder}' was refused, so {consequence}.");

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
				|| (exception is Win32Exception native && IsAccessDeniedCode(native.NativeErrorCode, Util.IsWindows)))
			{
				return exception;
			}

			if (exception is AggregateException aggregate)
				return aggregate.InnerExceptions.Select(FindRefusal).FirstOrDefault(found => found is not null);

			return FindRefusal(exception.InnerException);
		}

		// ERROR_ACCESS_DENIED on Windows, EACCES elsewhere; 5 is an I/O error on Unix.
		internal static bool IsAccessDeniedCode(int nativeErrorCode, bool windows)
			=> nativeErrorCode == (windows ? WindowsAccessDenied : UnixAccessDenied);

		/// <summary>The line of a child process's error output that reports a refused access.</summary>
		/// <param name="errorLines">The process's error lines.</param>
		/// <returns>That line, or null when none does.</returns>
		internal static string FindInOutput(IEnumerable<string> errorLines)
		{
			var lines = (errorLines ?? Enumerable.Empty<string>()).Where(line => line is not null).ToList();
			var refusal = lines.FirstOrDefault(NamesRefusal);

			// PowerShell prints its message, which can be translated, on the first line, and the exception type only
			// on its "+ CategoryInfo" and "+ FullyQualifiedErrorId" lines.
			return refusal is not null && IsPowerShellErrorDetail(refusal)
				? lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
				: refusal;
		}

		static bool NamesRefusal(string line)
			=> line.Contains(ExceptionName, StringComparison.Ordinal)
				|| line.Contains(SecurityExceptionName, StringComparison.Ordinal)
				|| line.Contains(DeniedRegistry, StringComparison.OrdinalIgnoreCase)
				|| (line.Contains(DeniedPathStart, StringComparison.OrdinalIgnoreCase) && line.Contains(DeniedPathEnd, StringComparison.OrdinalIgnoreCase));

		static bool IsPowerShellErrorDetail(string line)
			=> line.TrimStart().StartsWith("+ ", StringComparison.Ordinal);

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

		// The detail can be a raw line of a child process's output: keep the part that names the refusal, drop
		// control characters, and bound its length before it reaches a host's display.
		internal static string Summarize(string detail)
		{
			var text = (detail ?? string.Empty).Trim();

			var refusalStart = text.IndexOf(DeniedPathStart, StringComparison.OrdinalIgnoreCase);
			if (refusalStart > 0)
				text = text.Substring(refusalStart);

			var clean = new StringBuilder(text.Length);
			foreach (var character in text)
			{
				if (!char.IsControl(character))
					clean.Append(character);
			}

			return clean.Length <= MaxDetailLength
				? clean.ToString()
				: clean.ToString(0, MaxDetailLength) + "…";
		}
	}
}
