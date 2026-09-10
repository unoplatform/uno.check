#if !NET5_0_OR_GREATER

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Compiler shim: <c>init</c> accessors and positional records need this marker type, which
/// the netstandard2.0 reference assemblies do not carry. Declaring it here lets the same
/// source compile for older consumers without changing the public API.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit;

#endif
