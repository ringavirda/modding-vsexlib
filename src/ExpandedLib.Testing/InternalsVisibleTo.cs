using System.Runtime.CompilerServices;

// Reads what TestWorld.LoadAssets started and passed over, kept out of the harness's public API.
[assembly: InternalsVisibleTo("ExpandedLib.Tests")]

// Runs ViewGates.Findings over its whole domain.
[assembly: InternalsVisibleTo("IronIndustryExpanded.Tests")]
