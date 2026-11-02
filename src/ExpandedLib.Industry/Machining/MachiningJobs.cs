using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Machining;

/// <summary>Checks machining jobs against the loaded world and fills in what they derive from it.</summary>
public static class MachiningJobs {
  // Cubic metres in one voxel at 16 voxels to the metre.
  private const float CubicMetresPerVoxel = 1f / (16f * 16f * 16f);

  // Units of metal in one voxel (the density rule).
  private const float UnitsPerVoxel = 2.5f;

  /// <summary>
  /// Resolves every job in <paramref name="jobs"/> against <paramref name="collectibles"/>: finds its
  /// input and output, picks its cutting material (the job's, else the input's <c>cuttingMaterial</c>
  /// attribute) and sets <see cref="MachiningJob.RemovedKg"/> and <see cref="MachiningJob.ResolvedMaterial"/>.
  /// The removed mass is the job's <c>removed</c>, else (input <c>materialUnits</c> - count x output
  /// <c>materialUnits</c>) / 2.5 units per voxel x 1/4096 m3 per voxel x the material's density.
  /// </summary>
  /// <param name="collectibles">The loaded collectibles; null entries are skipped.</param>
  /// <param name="jobs">The jobs to resolve; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <param name="materials">The materials jobs may name; <see cref="CuttingMaterialRegistry.Shared"/> when null.</param>
  /// <returns>One message per job that cannot be resolved: an unknown input, output, fixture, byproduct or material, byproducts of more than one item, no
  /// material where one is needed, no units and no <c>removed</c>, or a removed mass of 0 or less.</returns>
  public static List<string> Resolve(
    IEnumerable<CollectibleObject?> collectibles,
    MachiningJobRegistry? jobs = null,
    CuttingMaterialRegistry? materials = null
  ) {
    jobs ??= MachiningJobRegistry.Shared;
    materials ??= CuttingMaterialRegistry.Shared;

    var byCode = new Dictionary<string, CollectibleObject>(
      StringComparer.OrdinalIgnoreCase
    );
    foreach (CollectibleObject? c in collectibles)
      if (c?.Code != null)
        byCode[c.Code.ToString()] = c;

    var errors = new List<string>();
    foreach (MachiningJob job in jobs.All) {
      string name = $"{job.Step} '{job.Input}'";
      if (!byCode.TryGetValue(job.Input, out CollectibleObject? input)) {
        errors.Add($"{name}: no such input item");
        continue;
      }
      if (!byCode.TryGetValue(job.Output, out CollectibleObject? output)) {
        errors.Add($"{name}: no such output item '{job.Output}'");
        continue;
      }

      if (job.Fixture is not null && !byCode.ContainsKey(job.Fixture)) {
        errors.Add($"{name}: no such fixture item '{job.Fixture}'");
        continue;
      }
      string? badByproduct = null;
      foreach (MachiningByproduct byproduct in job.Byproducts)
        if (!byCode.ContainsKey(byproduct.Code))
          badByproduct ??= $"no such byproduct item '{byproduct.Code}'";
      if (
        badByproduct is null
        && job.Byproducts.Select(b => b.Code)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .Count() > 1
      )
        badByproduct =
          "its byproducts name more than one item, and the chips slot holds one";
      if (badByproduct is not null) {
        errors.Add($"{name}: {badByproduct}");
        continue;
      }

      string? materialCode =
        job.Material
        ?? input.Attributes?[MachiningJob.MaterialAttribute].AsString();
      CuttingMaterial? material = null;
      if (materialCode != null && !materials.TryGet(materialCode, out material)) {
        errors.Add($"{name}: unknown cutting material '{materialCode}'");
        continue;
      }
      if (material is null && (job.Removed is null || job.Energy is null)) {
        errors.Add(
          $"{name}: names no cutting material, so its removed mass and energy cannot be derived"
        );
        continue;
      }

      float removed;
      if (job.Removed is { } declared)
        removed = declared;
      else {
        int inUnits =
          input.Attributes?[MachiningJob.UnitsAttribute].AsInt(0) ?? 0;
        int outUnits =
          output.Attributes?[MachiningJob.UnitsAttribute].AsInt(0) ?? 0;
        if (inUnits <= 0 || outUnits <= 0) {
          errors.Add(
            $"{name}: input or output carries no '{MachiningJob.UnitsAttribute}' and the job names no 'removed'"
          );
          continue;
        }
        removed =
          (inUnits - job.Count * outUnits)
          / UnitsPerVoxel
          * CubicMetresPerVoxel
          * material!.Density;
      }
      if (removed <= 0f) {
        errors.Add($"{name}: removes {removed} kg, which is not above 0");
        continue;
      }

      job.RemovedKg = removed;
      job.ResolvedMaterial = material?.Code;
    }
    return errors;
  }
}
