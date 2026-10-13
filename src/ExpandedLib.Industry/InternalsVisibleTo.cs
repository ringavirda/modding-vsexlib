using System.Runtime.CompilerServices;

// Drives the asset-free internal seams directly, not only through the public entry points.
[assembly: InternalsVisibleTo("ExpandedLib.Tests")]

// Resets the pipe tier registrations for a test that seeds its own.
[assembly: InternalsVisibleTo("ExpandedLib.Testing")]
