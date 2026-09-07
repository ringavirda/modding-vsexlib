using System.Runtime.CompilerServices;

// Castle DynamicProxy, which NSubstitute builds its substitutes with, must see this assembly's
// internal types to proxy a generic game type closed over one of them.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
