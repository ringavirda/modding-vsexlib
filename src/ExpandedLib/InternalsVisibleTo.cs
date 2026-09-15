using System.Runtime.CompilerServices;

// Drives internal mutation seams kept out of exlib's public API surface.
[assembly: InternalsVisibleTo("ExpandedLib.Tests")]

// Wraps test-only internal seams in public hooks beside the double or rig that uses them.
[assembly: InternalsVisibleTo("ExpandedLib.Testing")]
