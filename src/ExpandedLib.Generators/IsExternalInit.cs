// Polyfill for records and `init` accessors to compile on netstandard2.0.
namespace System.Runtime.CompilerServices {
  internal static class IsExternalInit { }
}
