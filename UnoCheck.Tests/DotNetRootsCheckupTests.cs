using System;
using System.IO;
using System.Runtime.InteropServices;
using DotNetCheck.Checkups;
using Xunit;

namespace UnoCheck.Tests
{
	/// <summary>
	/// Tests for the dotnet-roots divergence checkup internals (spec 001): PATH probing
	/// (empty/quoted/malformed entries), symlink resolution (chains and broken links), and
	/// root normalization.
	/// </summary>
	public class DotNetRootsCheckupTests : IDisposable
	{
		private readonly string _root;

		public DotNetRootsCheckupTests()
		{
			_root = Path.Join(Path.GetTempPath(), "uno-check-tests", Guid.NewGuid().ToString("n"));
			Directory.CreateDirectory(_root);
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(_root, recursive: true);
			}
			catch
			{
				// Best effort cleanup of the temp fixture.
			}
		}

		private string CreateDotnetDir(string name, string exeName = "dotnet")
		{
			var dir = Path.Combine(_root, name);
			Directory.CreateDirectory(dir);
			File.WriteAllText(Path.Combine(dir, exeName), string.Empty);
			return dir;
		}

		[Fact]
		public void ResolvePathDotnetRoot_FirstEntryCarryingExe_Wins()
		{
			var without = Path.Combine(_root, "no-dotnet-here");
			Directory.CreateDirectory(without);
			var first = CreateDotnetDir("first");
			var second = CreateDotnetDir("second");

			var pathValue = string.Join(Path.PathSeparator, without, first, second);

			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(pathValue, "dotnet");

			Assert.Equal(first, resolved);
		}

		[Fact]
		public void ResolvePathDotnetRoot_EmptyAndWhitespaceEntries_AreSkipped()
		{
			var dir = CreateDotnetDir("real");

			var pathValue = string.Join(Path.PathSeparator, "", "   ", dir, "");

			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(pathValue, "dotnet");

			Assert.Equal(dir, resolved);
		}

		[Fact]
		public void ResolvePathDotnetRoot_QuotedEntries_AreUnquoted()
		{
			var dir = CreateDotnetDir("quoted");

			var pathValue = $"\"{dir}\"";

			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(pathValue, "dotnet");

			Assert.Equal(dir, resolved);
		}

		[Fact]
		public void ResolvePathDotnetRoot_MalformedEntries_AreSkipped()
		{
			var dir = CreateDotnetDir("after-junk");

			var pathValue = string.Join(Path.PathSeparator, "\0junk", dir);

			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(pathValue, "dotnet");

			Assert.Equal(dir, resolved);
		}

		[Fact]
		public void ResolvePathDotnetRoot_NoMatch_ReturnsNull()
		{
			var without = Path.Combine(_root, "empty");
			Directory.CreateDirectory(without);

			Assert.Null(DotNetRootsCheckup.ResolvePathDotnetRoot(without, "dotnet"));
		}

		[Fact]
		public void ResolvePathDotnetRoot_RootedExeName_IsReducedToFileName()
		{
			var dir = CreateDotnetDir("rooted-exe");

			// A rooted exe segment must not override the PATH entry in Path.Combine.
			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(dir, Path.Combine(dir, "dotnet"));

			Assert.Equal(dir, resolved);
		}

		[Fact]
		public void ResolvePathDotnetRoot_SymlinkedExe_ResolvesToTargetRoot()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return; // Creating symlinks on Windows requires elevation.

			var realRoot = CreateDotnetDir("real-root");

			// PATH points at a bin dir whose dotnet is a two-hop symlink chain, mirroring
			// /usr/bin/dotnet → /usr/lib/dotnet/dotnet.
			var binDir = Path.Combine(_root, "bin");
			Directory.CreateDirectory(binDir);
			var intermediate = Path.Combine(_root, "intermediate-link");
			File.CreateSymbolicLink(intermediate, Path.Combine(realRoot, "dotnet"));
			File.CreateSymbolicLink(Path.Combine(binDir, "dotnet"), intermediate);

			var resolved = DotNetRootsCheckup.ResolvePathDotnetRoot(binDir, "dotnet");

			Assert.Equal(realRoot, resolved);
		}

		[Fact]
		public void ResolveLinks_RegularFile_ReturnsSamePath()
		{
			var file = Path.Combine(CreateDotnetDir("plain"), "dotnet");

			Assert.Equal(file, DotNetRootsCheckup.ResolveLinks(file));
		}

		[Fact]
		public void ResolveLinks_NonExistentFile_FallsBackToLiteralPath()
		{
			var file = Path.Combine(_root, "does-not-exist", "dotnet");

			Assert.Equal(file, DotNetRootsCheckup.ResolveLinks(file));
		}

		[Fact]
		public void ResolveLinks_BrokenLink_FallsBackGracefully()
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return; // Creating symlinks on Windows requires elevation.

			var link = Path.Combine(_root, "broken-link");
			File.CreateSymbolicLink(link, Path.Combine(_root, "gone", "dotnet"));

			// Must not throw; either the (dangling) target or the literal path is acceptable.
			Assert.NotNull(DotNetRootsCheckup.ResolveLinks(link));
		}

		[Fact]
		public void NormalizeRoot_TrailingSeparators_AreTrimmed()
		{
			var expected = Path.Combine(_root, "sdk-root");

			Assert.Equal(expected, DotNetRootsCheckup.NormalizeRoot(expected + Path.DirectorySeparatorChar));
		}

		[Fact]
		public void NormalizeRoot_NullOrEmpty_ReturnsNull()
		{
			Assert.Null(DotNetRootsCheckup.NormalizeRoot(null));
			Assert.Null(DotNetRootsCheckup.NormalizeRoot(string.Empty));
		}

		[Fact]
		public void NormalizeRoot_RelativeSegments_AreResolved()
		{
			var expected = Path.Combine(_root, "sdk-root");

			var normalized = DotNetRootsCheckup.NormalizeRoot(Path.Combine(_root, "other", "..", "sdk-root"));

			Assert.Equal(expected, normalized);
		}

		[Fact]
		public void NormalizeRoot_MalformedPath_ReturnsNull()
		{
			// e.g. a broken DOTNET_ROOT value; must not throw out of the checkup.
			Assert.Null(DotNetRootsCheckup.NormalizeRoot("bad\0path"));
		}
	}
}
