using ExpandedLib.Registries;

// Ships inside the exlib mod folder, under the exlib asset domain.
[assembly: ExDomain("exlib")]

// A framework module, hosted by exlib itself. Its Mod is exlib's, not "industry".
[assembly: ExModule("industry", Mod = "exlib")]
