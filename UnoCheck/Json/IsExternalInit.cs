#if !NET5_0_OR_GREATER
// Enables C# 9 init-only setters on netcoreapp3.1, where the runtime does not ship this marker type.
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit
	{
	}
}
#endif
