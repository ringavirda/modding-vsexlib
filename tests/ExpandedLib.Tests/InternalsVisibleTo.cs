using System.Runtime.CompilerServices;

// NSubstitute (via Castle DynamicProxy) needs this assembly's internals to proxy a generic game type closed over one.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
