using Spectre.Console;

namespace DotNetCheck
{
	public static class Icon
	{
		public static string PrettyBoring(string pretty, string boring)
			=> (Util.IsWindows || Util.IsLinux) ? boring : pretty;

		public static string Error
			=> PrettyBoring(":cross_mark:", "×");
		public static string Warning
			=> PrettyBoring(":dragon:", "¡");

		public static string ListItem
			=> "–";

		public static string Checking
			=> PrettyBoring(":magnifying_glass_tilted_right:", "›");

		public static string Ignored
			=> PrettyBoring($":{Emoji.Known.RightArrowCurvingDown}:", "@");

		public static string Recommend
			=> PrettyBoring(":syringe:", "¤");

		public static string Success
			=> PrettyBoring(":check_mark:", "–");

		public static string Ambulance
			=> PrettyBoring(":ambulance:", "¤");

		public static string Bell
			=> PrettyBoring("\a:bell:", "\a!");

		public static string Thinking
			=> PrettyBoring(":hourglass_not_done:", "»");
	}


}
