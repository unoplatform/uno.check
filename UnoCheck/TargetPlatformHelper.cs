#nullable enable

using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck
{
	public static class TargetPlatformHelper
	{
		public static TargetPlatform GetTargetPlatformsFromFlags(string[]? targetPlatformFlags)
		{
			if (targetPlatformFlags == null || targetPlatformFlags.Length == 0)
			{
				return TargetPlatform.All;
			}

			var output = TargetPlatform.None;

			foreach (var flag in targetPlatformFlags)
			{
				output |= GetTargetPlatformFromFlag(flag);
			}

			return output;
		}

		private static TargetPlatform GetTargetPlatformFromFlag(string flag)
		{
			switch (flag.ToLowerInvariant())
			{
				case "webassembly":
				case "wasm":
					return TargetPlatform.WebAssembly;
				case "ios":
					return TargetPlatform.iOS;
				case "android":
				case "droid":
					return TargetPlatform.Android;
				case "macos":
					return TargetPlatform.macOS;
				case "skiawpf":
				case "skia-wpf":
					return TargetPlatform.SkiaWPF;
				case "skiagtk":
				case "skia-gtk":
					return TargetPlatform.SkiaGtk;
				case "skiatizen":
				case "skia-tizen":
					return TargetPlatform.SkiaTizen;
				case "uwp":
					return TargetPlatform.UWP;
				case "win32desktop":
				case "win32":
					return TargetPlatform.Win32Desktop;

				case "skia":
					return TargetPlatform.SkiaWPF | TargetPlatform.SkiaGtk | TargetPlatform.SkiaTizen;
				case "linux":
					return TargetPlatform.SkiaGtk;

				case "all":
					return TargetPlatform.All;

				default:
					return TargetPlatform.None;
			}
		}
	}
}
