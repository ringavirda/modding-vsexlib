using System.Runtime.CompilerServices;

// Drives internal mutation seams kept out of exlib's public API surface.
[assembly: InternalsVisibleTo("ExpandedLib.Tests")]

// Wraps test-only internal seams in public hooks beside the double or rig that uses them, and
// reads setup files through the Scenarios model.
[assembly: InternalsVisibleTo("ExpandedLib.Testing")]

// Reads the block signals, missing cells and tree comparison the live scenario runner drives, and
// the setup files it runs.
[assembly: InternalsVisibleTo("ExpandedLib.Testing.Live")]

// Logs a replaced registration through the registration log.
[assembly: InternalsVisibleTo("exlib.industry")]
