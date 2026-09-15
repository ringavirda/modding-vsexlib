using ExpandedLib.Registries;

// The domain every registrable type in this assembly is keyed under, and the assets/<domain>/ tree it
// ships as. Declared here rather than derived from the mod id at registration time so a key resolves
// with no load-order dependency: a dependent mod naming one of this assembly's classes through
// ExBlockDef.Class<T>() gets "exlib.Xxx" even if its Start has not run yet.
[assembly: ExDomain("exlib")]
