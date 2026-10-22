#if GAME_GE_1_22
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>An <see cref="ITerrainMeshPool"/> that keeps every mesh it is handed instead of
/// uploading it.</summary>
internal sealed class RecordingMeshPool : ITerrainMeshPool {
  private readonly List<PooledMesh> _meshes = [];

  internal IReadOnlyList<PooledMesh> Meshes => _meshes;

  public void AddMeshData(MeshData data, int lodLevel = 1) =>
    _meshes.Add(new PooledMesh(data, lodLevel, false));

  public void AddMeshData(MeshData data, float[] tfMatrix, int lodLevel = 1) =>
    _meshes.Add(
      new PooledMesh(data.Clone().MatrixTransform(tfMatrix), lodLevel, false)
    );

  public void AddMeshData(
    MeshData data,
    ColorMapData colorMapData,
    int lodLevel = 1
  ) => _meshes.Add(new PooledMesh(data, lodLevel, false));

  internal void AddBlockMesh(MeshData data) =>
    _meshes.Add(new PooledMesh(data, 1, true));
}
#endif
