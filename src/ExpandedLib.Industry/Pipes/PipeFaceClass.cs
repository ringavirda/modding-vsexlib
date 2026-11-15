namespace ExpandedLib.Industry.Pipes;

/// <summary>What lies across an open face of a pipe node, as <see cref="PipeFaces.Classify"/> sorts it.</summary>
public enum PipeFaceClass {
  /// <summary>The strategy that serves the node claims the face as a vent.</summary>
  Vent = 0,

  /// <summary>No block, or air.</summary>
  Air = 1,

  /// <summary>A structure cell: a filler or a block hosting fillers.</summary>
  Structure = 2,

  /// <summary>A block whose whole face toward the node is solid, <see cref="ExpandedLib.Networks.NetworkFaces.ClosesOpening"/>.</summary>
  Solid = 3,

  /// <summary>Any other block: a pipe of a refused joint, a slab, a fence, a machine face with no port.</summary>
  Other = 4,
}
