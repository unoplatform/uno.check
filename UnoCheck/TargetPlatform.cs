using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetCheck
{
	[Flags]
	public enum TargetPlatform
	{
		None = 0,
		WebAssembly = 1,
		iOS = 2,
		Android = 4,
		macOS = 8,
		SkiaWPF = 16,
		SkiaGtk = 32,
		SkiaTizen = 64,
		Windows = 128,
		WinAppSDK = 256,

		All = -1
	}
}
