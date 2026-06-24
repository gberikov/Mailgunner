#if !NET8_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Compiler shim that enables <c>init</c>-only property setters on target frameworks (such as
/// <c>netstandard2.0</c>) whose base class library does not ship <c>IsExternalInit</c>. Required by
/// the C# compiler; not part of the public surface. No effect on modern targets, where the real type
/// is provided by the runtime.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
