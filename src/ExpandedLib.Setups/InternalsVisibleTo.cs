using System.Runtime.CompilerServices;

// Wraps the model in public hooks and drives it from the in-process rig.
[assembly: InternalsVisibleTo("ExpandedLib.Testing")]

// Runs setup files against a live server.
[assembly: InternalsVisibleTo("ExpandedLib.Testing.Live")]

// Tests the model directly.
[assembly: InternalsVisibleTo("ExpandedLib.Tests")]
