using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Collection definitions for the process-wide static registries, one per registry.</summary>
[CollectionDefinition("MetalRegistry", DisableParallelization = true)]
public class MetalRegistryCollection { }

[CollectionDefinition("MaterialRoles", DisableParallelization = true)]
public class MaterialRolesCollection { }

[CollectionDefinition("ExLiquids", DisableParallelization = true)]
public class ExLiquidsCollection { }

[CollectionDefinition("ExDefinitions", DisableParallelization = true)]
public class ExDefinitionsCollection { }

[CollectionDefinition("ExCheckRegistry", DisableParallelization = true)]
public class ExCheckRegistryCollection { }
