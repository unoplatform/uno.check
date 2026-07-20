using System;
using System.IO;
using System.Linq;
using DotNetCheck.Checkups;
using Xunit;

namespace UnoCheck.Tests
{
	/// <summary>
	/// Tests for the targeting-pack alignment checkup internals (spec 001): manifest
	/// enumeration across band layouts, pinned-version extraction, and pack probing.
	/// </summary>
	public class DotNetTargetingPackAlignmentTests : IDisposable
	{
		private readonly string _root;

		public DotNetTargetingPackAlignmentTests()
		{
			_root = Path.Combine(Path.GetTempPath(), "uno-check-tests", Guid.NewGuid().ToString("n"));
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

		private const string MisalignedManifest = /*lang=json*/ """
			{
				"version": "10.0.105",
				"depends-on": { "Microsoft.NET.Workload.Emscripten.Current": "10.0.105" },
				"packs": {
					"Microsoft.NETCore.App.Runtime.Mono.browser-wasm": {
						"kind": "framework",
						"version": "10.0.5"
					},
					"Microsoft.NET.Runtime.WebAssembly.Sdk": {
						"kind": "sdk",
						"version": "10.0.5"
					}
				}
			}
			""";

		private const string ManifestWithoutBrowserWasm = /*lang=json*/ """
			{
				"version": "10.0.105",
				"packs": {
					"Microsoft.NET.Runtime.MonoAOTCompiler.Task": {
						"kind": "sdk",
						"version": "10.0.5"
					}
				}
			}
			""";

		[Fact]
		public void GetPinnedBrowserWasmRuntimeVersion_BrowserWasmPack_ReturnsVersion()
		{
			var version = DotNetTargetingPackAlignmentCheckup.GetPinnedBrowserWasmRuntimeVersion(MisalignedManifest);

			Assert.Equal("10.0.5", version);
		}

		[Fact]
		public void GetPinnedBrowserWasmRuntimeVersion_NoBrowserWasmPack_ReturnsNull()
		{
			var version = DotNetTargetingPackAlignmentCheckup.GetPinnedBrowserWasmRuntimeVersion(ManifestWithoutBrowserWasm);

			Assert.Null(version);
		}

		[Fact]
		public void EnumerateMonoToolchainManifests_FlatAndVersionedLayouts_FindsBoth()
		{
			// Flat (older SDKs): sdk-manifests/<band>/<id>/WorkloadManifest.json
			var flat = Path.Combine(_root, "sdk-manifests", "9.0.100", "microsoft.net.workload.mono.toolchain.current");
			Directory.CreateDirectory(flat);
			File.WriteAllText(Path.Combine(flat, "WorkloadManifest.json"), MisalignedManifest);

			// Versioned (newer SDKs): sdk-manifests/<band>/<id>/<manifest-version>/WorkloadManifest.json
			var versioned = Path.Combine(_root, "sdk-manifests", "10.0.100", "microsoft.net.workload.mono.toolchain.current", "10.0.105");
			Directory.CreateDirectory(versioned);
			File.WriteAllText(Path.Combine(versioned, "WorkloadManifest.json"), MisalignedManifest);

			// Unrelated manifest id: must not be picked up.
			var other = Path.Combine(_root, "sdk-manifests", "10.0.100", "microsoft.net.sdk.android", "36.1.43");
			Directory.CreateDirectory(other);
			File.WriteAllText(Path.Combine(other, "WorkloadManifest.json"), MisalignedManifest);

			var manifests = DotNetTargetingPackAlignmentCheckup.EnumerateMonoToolchainManifests(_root).ToList();

			Assert.Equal(2, manifests.Count);
			Assert.All(manifests, m => Assert.Contains("microsoft.net.workload.mono.toolchain.current", m));
		}

		[Fact]
		public void EnumerateMonoToolchainManifests_NoManifestsFolder_Empty()
		{
			var manifests = DotNetTargetingPackAlignmentCheckup.EnumerateMonoToolchainManifests(_root);

			Assert.Empty(manifests);
		}

		[Fact]
		public void IsTargetingPackAvailable_InstalledInPacks_True()
		{
			Directory.CreateDirectory(Path.Combine(_root, "packs", "Microsoft.NETCore.App.Ref", "10.0.5"));

			Assert.True(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, "10.0.5"));
		}

		[Fact]
		public void IsTargetingPackAvailable_InNugetCache_True()
		{
			var nugetRoot = Path.Combine(_root, "nuget-cache");
			Directory.CreateDirectory(Path.Combine(nugetRoot, "microsoft.netcore.app.ref", "10.0.5"));

			var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			try
			{
				Environment.SetEnvironmentVariable("NUGET_PACKAGES", nugetRoot);

				Assert.True(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, "10.0.5"));
			}
			finally
			{
				Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
			}
		}

		[Fact]
		public void IsTargetingPackAvailable_MalformedVersion_False()
		{
			// A parsed-manifest version that is empty or rooted must fail closed instead of
			// probing outside the expected roots.
			Assert.False(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, ""));
			Assert.False(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, "   "));
			Assert.False(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, Path.GetTempPath()));
		}

		[Fact]
		public void IsTargetingPackAvailable_NowhereToBeFound_False()
		{
			// Field case: manifest pins 10.0.5 while only 10.0.7 is installed.
			Directory.CreateDirectory(Path.Combine(_root, "packs", "Microsoft.NETCore.App.Ref", "10.0.7"));

			var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			try
			{
				Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine(_root, "empty-nuget-cache"));

				Assert.False(DotNetTargetingPackAlignmentCheckup.IsTargetingPackAvailable(_root, "10.0.5"));
			}
			finally
			{
				Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
			}
		}
	}
}
