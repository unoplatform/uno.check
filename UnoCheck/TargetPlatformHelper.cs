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
        /// <summary>
        /// Converts the string provided uno-check input argument to the corresponding <see cref="TargetPlatform"/>
        /// </summary>
        /// <param name="flag">The user-provided argument as <see langword="string"/></param>
        /// <returns>The <see cref="TargetPlatform"/> from the <paramref name="flag"/></returns>
		public static TargetPlatform GetTargetPlatformFromFlag(string flag)
		{
			switch (flag.ToLowerInvariant())
			{
                case "web":
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
                case "windows":
                case "wasdk":
					return TargetPlatform.WinAppSDK;
                case "win32desktop":
                case "win32":
                    return TargetPlatform.Windows;
                case "skiadesktop":
				case "skia":
					return TargetPlatform.SkiaDesktop;
				case "linux":
					return TargetPlatform.SkiaDesktop;
				case "all":
					return TargetPlatform.All;
				default:
					return TargetPlatform.None;
			}
		}
	}
}
