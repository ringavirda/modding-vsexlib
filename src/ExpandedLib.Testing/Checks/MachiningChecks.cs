#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// Checks over a loaded machining catalogue (jobs, machine kinds, hand routes), one finding per
/// fault, empty when clean; called with no registry after <see cref="TestWorld.LoadAsGame"/>, they
/// read the loaded catalogue.
/// </summary>
public static class MachiningChecks
{
  // Held seconds of the hand route may differ from the energy figure by this share.
  private const float HandTolerance = 0.1f;

  /// <summary>Every job's step is offered by a machine kind, or the job names a hand route.</summary>
  /// <param name="jobs">The jobs read; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <param name="kinds">The kinds read; <see cref="MachineKindRegistry.Shared"/> when null.</param>
  /// <returns>One finding per job that no kind offers and that names no hand route; a job whose
  /// <c>machines</c> list names no kind declaring its step counts as offered by none.</returns>
  public static IReadOnlyList<string> Offered(
    MachiningJobRegistry? jobs = null,
    MachineKindRegistry? kinds = null
  )
  {
    jobs ??= MachiningJobRegistry.Shared;
    kinds ??= MachineKindRegistry.Shared;
    var findings = new List<string>();
    foreach (MachiningJob job in jobs.All)
      if (job.Hand is null && OfferedAt(job, kinds).Count == 0)
        findings.Add(
          $"{Name(job)}: no machine kind offers the step and the job names no hand route"
        );
    return findings;
  }

  /// <summary>The hand route of each job is a grid recipe that takes the job's input and gives its
  /// output, and its craft sequence holds the energy figure's time.</summary>
  /// <param name="world">The loaded world whose grid recipes are read.</param>
  /// <param name="jobs">The jobs read, resolved against the world; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <param name="kinds">The kinds read; <see cref="MachineKindRegistry.Shared"/> when null.</param>
  /// <param name="materials">The cutting materials and hand power read; <see cref="CuttingMaterialRegistry.Shared"/> when null.</param>
  /// <param name="sequences">The craft sequences read; <see cref="CraftSequenceRegistry.Shared"/> when null.</param>
  /// <returns>One finding per job whose route is no grid recipe, has no craft sequence, takes a
  /// different input (the recipe's one ingredient that is not a tool) or gives a different output
  /// or count, or whose held seconds (<see cref="CraftStep.Duration"/>, the tool default's stroke for
  /// hit steps) differ from E / hand power / 30 by more than 10 % at any kind's motion. Jobs without a
  /// hand route are not read.</returns>
  public static IReadOnlyList<string> HandParity(
    TestWorld world,
    MachiningJobRegistry? jobs = null,
    MachineKindRegistry? kinds = null,
    CuttingMaterialRegistry? materials = null,
    CraftSequenceRegistry? sequences = null
  )
  {
    jobs ??= MachiningJobRegistry.Shared;
    kinds ??= MachineKindRegistry.Shared;
    materials ??= CuttingMaterialRegistry.Shared;
    sequences ??= CraftSequenceRegistry.Shared;
    var findings = new List<string>();
    foreach (MachiningJob job in jobs.All)
    {
      if (job.Hand is null)
        continue;
      string name = Name(job);
      GridRecipe? recipe = Recipe(world, job.Hand.Recipe);
      if (recipe is null)
      {
        findings.Add(
          $"{name}: hand route '{job.Hand.Recipe}' is no grid recipe"
        );
        continue;
      }
      CraftSequence? sequence = sequences.Sequence(
        job.Hand.Machine,
        job.Hand.Recipe
      );
      if (sequence is null)
      {
        findings.Add(
          $"{name}: hand route '{job.Hand.Recipe}' has no craft sequence at '{job.Hand.Machine}'"
        );
        continue;
      }

      string[] inputs =
      [
        .. recipe
          .ResolvedIngredients?.Where(i => i is { IsTool: false })
          .Select(i => i!.Code!.ToString())
          .Distinct(StringComparer.OrdinalIgnoreCase)
          ?? [],
      ];
      if (inputs.Length != 1 || !Same(inputs[0], job.Input))
        findings.Add(
          $"{name}: hand route '{job.Hand.Recipe}' takes [{string.Join(", ", inputs)}], not the job's input"
        );
      string output = recipe.Output?.Code?.ToString() ?? "";
      int count = recipe.Output?.StackSize ?? 0;
      if (!Same(output, job.Output) || count != job.Count)
        findings.Add(
          $"{name}: hand route '{job.Hand.Recipe}' gives {count} x {output}, the job {job.Count} x {job.Output}"
        );

      float held = HeldSeconds(sequence, sequences);
      foreach (
        CutMotion motion in OfferedAt(job, kinds)
          .Select(k => k.StepOf(job.Step)!.Motion)
          .Distinct()
      )
      {
        double expected = MachineCut.HandSeconds(
          MachineCut.Energy(job, motion, materials),
          materials.HandPower(motion)
        );
        if (
          Math.Abs(held - expected) > HandTolerance * expected
          || expected <= 0d
        )
          findings.Add(
            $"{name}: hand route '{job.Hand.Recipe}' is held {held:0.##} s, the {motion} energy figure gives {expected:0.##} s"
          );
      }
    }
    return findings;
  }

  /// <summary>At every kind that offers a job's step the machine wears its tool less and finishes
  /// sooner than the hand route.</summary>
  /// <param name="world">The loaded world whose grid recipes are read.</param>
  /// <param name="jobs">The jobs read, resolved against the world; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <param name="kinds">The kinds read; <see cref="MachineKindRegistry.Shared"/> when null.</param>
  /// <param name="materials">The cutting materials read; <see cref="CuttingMaterialRegistry.Shared"/> when null.</param>
  /// <param name="sequences">The craft sequences read; <see cref="CraftSequenceRegistry.Shared"/> when null.</param>
  /// <returns>One finding per job and kind whose wear points are not below the hand sequence's
  /// steps that hold a tool, or whose real seconds, E / cut power / 30, are not below the sequence's
  /// held seconds; the finding prints both numbers. Jobs whose hand route is missing are left to
  /// <see cref="HandParity"/>.</returns>
  public static IReadOnlyList<string> MachinesFirst(
    TestWorld world,
    MachiningJobRegistry? jobs = null,
    MachineKindRegistry? kinds = null,
    CuttingMaterialRegistry? materials = null,
    CraftSequenceRegistry? sequences = null
  )
  {
    jobs ??= MachiningJobRegistry.Shared;
    kinds ??= MachineKindRegistry.Shared;
    materials ??= CuttingMaterialRegistry.Shared;
    sequences ??= CraftSequenceRegistry.Shared;
    var findings = new List<string>();
    foreach (MachiningJob job in jobs.All)
    {
      if (job.Hand is null)
        continue;
      CraftSequence? sequence = sequences.Sequence(
        job.Hand.Machine,
        job.Hand.Recipe
      );
      if (sequence is null)
        continue;
      float held = HeldSeconds(sequence, sequences);
      int handWear = sequence.Steps.Count(s => s.Tool != null);
      foreach (MachineKind kind in OfferedAt(job, kinds))
      {
        MachineKindStep step = kind.StepOf(job.Step)!;
        double seconds =
          MachineCut.Energy(job, step.Motion, materials)
          / step.Power
          / MachineCut.GameSecondsPerRealSecond;
        if (job.Wear >= handWear)
          findings.Add(
            $"{Name(job)} at {kind.Code}: the machine wears its tool {job.Wear} points, the hand route's {handWear} tool steps"
          );
        if (seconds >= held)
          findings.Add(
            $"{Name(job)} at {kind.Code}: the machine takes {seconds:0.##} s, the hand route {held:0.##} s"
          );
      }
    }
    return findings;
  }

  /// <summary>The input's drawn extents fit the step's envelope at every kind the job is offered at.</summary>
  /// <param name="world">The loaded world whose items and blocks are read.</param>
  /// <param name="jobs">The jobs read; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <param name="kinds">The kinds read; <see cref="MachineKindRegistry.Shared"/> when null.</param>
  /// <param name="shapeFile">Resolves a shape base such as <c>game:item/ingot</c> to its file; null
  /// reads <c>game:</c> shapes from the game install and other domains from <see cref="RepoPaths.Assets"/>.</param>
  /// <returns>One finding per job and kind whose step declares an envelope and whose input's longest
  /// extent in voxels (<see cref="ShapeExtents.Of"/>, any axis) exceeds the envelope's length or whose
  /// second longest exceeds its across; one per input that is no loaded item or block, has no shape
  /// or whose shape file is missing. A job's <c>machines</c> list narrows the kinds read, and a
  /// step with no envelope accepts any piece.</returns>
  public static IReadOnlyList<string> Envelopes(
    TestWorld world,
    MachiningJobRegistry? jobs = null,
    MachineKindRegistry? kinds = null,
    System.Func<string, string>? shapeFile = null
  )
  {
    jobs ??= MachiningJobRegistry.Shared;
    kinds ??= MachineKindRegistry.Shared;
    shapeFile ??= GameShapeFile;
    var findings = new List<string>();
    foreach (MachiningJob job in jobs.All)
    {
      List<(MachineKind Kind, MachineEnvelope Envelope)> bounded =
      [
        .. OfferedAt(job, kinds)
          .Where(k => k.StepOf(job.Step)!.Envelope != null)
          .Select(k => (k, k.StepOf(job.Step)!.Envelope!)),
      ];
      if (bounded.Count == 0)
        continue;
      string? problem = Extents(world, job.Input, shapeFile, out float[] dims);
      if (problem != null)
      {
        findings.Add($"{Name(job)}: {problem}");
        continue;
      }
      foreach ((MachineKind kind, MachineEnvelope envelope) in bounded)
        if (dims[0] > envelope.Length || dims[1] > envelope.Across)
          findings.Add(
            $"{Name(job)} at {kind.Code}: the piece is {dims[0]:0.##} x {dims[1]:0.##} voxels, the step takes {envelope.Length:0.##} long and {envelope.Across:0.##} across"
          );
    }
    return findings;
  }

  /// <summary>A job's byproducts hold no more metal than it removes.</summary>
  /// <param name="world">The loaded world whose items and blocks are read.</param>
  /// <param name="jobs">The jobs read; <see cref="MachiningJobRegistry.Shared"/> when null.</param>
  /// <returns>One finding per job whose byproducts' <c>materialUnits</c> summed over their counts
  /// exceed the input's units less the output's units times the job's count. A job whose input or
  /// output carries no units, or whose byproduct is not loaded, is left to
  /// <see cref="MachiningJobs.Resolve(IEnumerable{CollectibleObject?}, MachiningJobRegistry?, CuttingMaterialRegistry?)"/>.</returns>
  public static IReadOnlyList<string> CuttingsMass(
    TestWorld world,
    MachiningJobRegistry? jobs = null
  )
  {
    jobs ??= MachiningJobRegistry.Shared;
    var findings = new List<string>();
    foreach (MachiningJob job in jobs.All)
    {
      if (job.Byproducts.Count == 0)
        continue;
      int? input = Units(world, job.Input);
      int? output = Units(world, job.Output);
      if (input is null || output is null)
        continue;
      int removed = input.Value - job.Count * output.Value;
      int cuttings = 0;
      bool known = true;
      foreach (MachiningByproduct byproduct in job.Byproducts)
      {
        int? units = Units(world, byproduct.Code);
        if (units is null)
          known = false;
        else
          cuttings += units.Value * byproduct.Count;
      }
      if (known && cuttings > removed)
        findings.Add(
          $"{Name(job)}: the byproducts hold {cuttings} units, the cut removes {removed}"
        );
    }
    return findings;
  }

  private static string Name(MachiningJob job) => $"{job.Step} '{job.Input}'";

  private static bool Same(string a, string b) =>
    string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

  private static List<MachineKind> OfferedAt(
    MachiningJob job,
    MachineKindRegistry kinds
  ) => [.. kinds.Offering(job.Step).Where(k => MachineCut.OffersTo(job, k))];

  private static GridRecipe? Recipe(TestWorld world, string name) =>
    world.World.GridRecipes.FirstOrDefault(r =>
      Same(r.Name?.ToString() ?? "", name)
    );

  private static float HeldSeconds(
    CraftSequence sequence,
    CraftSequenceRegistry sequences
  ) =>
    sequence.Steps.Sum(s =>
      s.Duration(
        sequences.ToolDefault(s.Tool)?.Stroke ?? CraftToolDefault.DefaultStroke
      )
    );

  private static CollectibleObject? Find(TestWorld world, string code)
  {
    var location = new AssetLocation(code);
    return (CollectibleObject?)world.GetItem(location)
      ?? world.World.GetBlock(location);
  }

  private static int? Units(TestWorld world, string code)
  {
    int units =
      Find(world, code)?.Attributes?[MachiningJob.UnitsAttribute].AsInt(0) ?? 0;
    return units > 0 ? units : null;
  }

  // The extents, longest first, or what stops them being read.
  private static string? Extents(
    TestWorld world,
    string code,
    System.Func<string, string> shapeFile,
    out float[] dims
  )
  {
    dims = [];
    CollectibleObject? collectible = Find(world, code);
    if (collectible is null)
      return "no such input item";
    AssetLocation? shape = collectible switch
    {
      Item item => item.Shape?.Base,
      Block block => block.Shape?.Base,
      _ => null,
    };
    if (shape is null)
      return "the input has no shape, so its extents cannot be read";
    string path = shapeFile(shape.ToString());
    if (!File.Exists(path))
      return $"the input's shape file {path} is missing";
    (float width, float thickness, float length) = ShapeExtents.Of(path);
    dims = [.. new[] { width, thickness, length }.OrderDescending()];
    return null;
  }

  [CheckHelper("resolves a shape base to its file")]
  public static string GameShapeFile(string shapeBase)
  {
    string[] parts = shapeBase.Split(':', 2);
    string relative = Path.Combine(
      "shapes",
      (parts[1] + ".json").Replace('/', Path.DirectorySeparatorChar)
    );
    if (parts[0] != "game")
      return Path.Combine(RepoPaths.Assets(parts[0]), relative);
    string assets = Path.Combine(
      VsAssemblyResolver.InstallPath ?? "",
      "assets"
    );
    string survival = Path.Combine(assets, "survival", relative);
    return File.Exists(survival)
      ? survival
      : Path.Combine(assets, "game", relative);
  }
}
#endif
