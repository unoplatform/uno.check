using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotNetCheck.AndroidSdk
{
	public partial class AvdManager
	{
		/// <summary>
		/// AVD Info
		/// </summary>
		public class Avd
		{
			/// <summary>
			/// Gets or sets the name.
			/// </summary>
			/// <value>The name.</value>
			public string Name { get; set; }

			public string Device { get; set; }
			public string Path { get; set; }
			public string Target { get; set; }

			public string BasedOn { get; set; }

			public override string ToString()
			{
				return $"{Name} | {Device} | {Target} | {Path} | {BasedOn}";
			}

			public string SdkId { get; internal set; }

			/// <summary>
			/// Reads the AVD in <paramref name="avdPath"/>, or returns null when the folder has no config.ini.
			/// </summary>
			public static Avd From(string avdPath)
			{
				var configIniFile = System.IO.Path.Join(avdPath, "config.ini");

				if (!File.Exists(configIniFile))
					return null;

				var configIni = ParseIni(configIniFile);

				var avd = new Avd();
				avd.Path = avdPath;
				string avdIniFile = "";
				if (configIni.TryGetValue("AvdId", out var avdName))
				{
					avd.Name = avdName;
					avdIniFile = System.IO.Path.Join(avdPath, "..", avdName + ".ini");
				}
				else
				{
					var configIniFolder = System.IO.Path.GetDirectoryName(configIniFile);
					avdIniFile = System.IO.Path.Join(avdPath, "..", System.IO.Path.GetFileNameWithoutExtension(configIniFolder) + ".ini");
				}

				if (configIni.TryGetValue("hw.device.name", out var avdDevice))
					avd.Device = avdDevice;
				if (configIni.TryGetValue("image.sysdir.1", out var avdBasedOn))
					avd.SdkId = avdBasedOn?.Trim('\\', '/')?.Replace('\\', ';')?.Replace('/', ';');

				// The .ini sits next to the folder only in the AVD home. An AVD created elsewhere
				// (avdmanager create avd -p) keeps it in the AVD home, so the target is left unset.
				if (File.Exists(avdIniFile) && ParseIni(avdIniFile).TryGetValue("target", out var avdTarget))
					avd.Target = avdTarget;

				return avd;
			}

			internal static Dictionary<string, string> ParseIni(string filename)
			{
				var d = new Dictionary<string, string>();

				if (File.Exists(filename))
				{
					foreach (var line in File.ReadAllLines(filename))
					{
						var parts = line.Split(new char[] { '=' }, 2);

						// Blank lines and lines without a value are skipped.
						if (parts.Length == 2)
							d[parts[0].Trim()] = parts[1].Trim();
					}
				}

				return d;
			}
		}
	}
}
