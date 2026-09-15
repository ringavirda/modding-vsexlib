namespace ExpandedLib.Structures;

/// <summary>A functional component of a multiblock machine whose own block entity is not the anchor but belongs to one.</summary>
public interface IMultiblockComponent {
  /// <summary>The multiblock anchor whose layout owns this component's cell, or null when none is in range.</summary>
  BlockEntityMultiblockStructure? ResolveOwningAnchor();
}
