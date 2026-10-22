#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Checks;
using ExpandedLib.Structures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>Every block draws something inside its footprint, in every facing and every stage the
/// player meets it in, as the game's chunk tesselator keeps it (<see cref="DrawnCell"/>).</summary>
public static class DrawLaw
{
  internal const string Name = "draw";

  /// <summary>Draws each distinct variant of each block of <paramref name="domain"/> placed through
  /// the accessor's <c>SetBlock</c> with a stack of the block, then at each construction stage
  /// paid in the first facing, then, for a multiblock anchor, formed in the first facing, every
  /// cell of the formed structure.</summary>
  /// <remarks>Turns <see cref="TestWorld.ClientTesselates"/> and
  /// <see cref="TestWorld.SpawnInitializesBeforeRegistering"/> on. Variants sharing block class,
  /// entity, draw type, shape and facing collapse to the first by code; a structure filler is no
  /// case, nor is a structure filler among the formed cells. A block of a non-JSON draw type is judged for an overridden <c>OnTesselation</c> only.
  /// A placement, payment or formation that throws or logs a warning is a finding.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged and of the blocks their
  /// layouts name (<see cref="BlockLaws.Run"/> stands one), able to read the blocks' shapes.</param>
  /// <param name="domain">The domain whose blocks are drawn.</param>
  /// <returns>The law's blocktypes, cells drawn and findings, each keyed by the variant's
  /// code.</returns>
  /// <exception cref="InvalidOperationException">No game install resolves.</exception>
  public static BlockLaws.Law Run(TestWorld world, string domain)
  {
    world.ClientTesselates = true;
    world.SpawnInitializesBeforeRegistering = true;
    var findings = new List<string>();
    var seen = new HashSet<string>();
    var sites = new BlockLaws.Sites();
    Block[] registered = MultiblockLaw.Registered(world);
    int blocks = 0,
      cases = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    )
    {
      Block[] kept =
      [
        .. type.Where(b => b is not BlockStructureFiller)
          .DistinctBy(b => VariantKey(world, b)),
      ];
      if (kept.Length == 0)
        continue;
      blocks++;
      HashSet<Block> firstFacing = [.. MultiblockLaw.FirstFacing(kept)];
      void Report(string line)
      {
        if (seen.Add(line))
          findings.Add(line);
      }

      foreach (Block block in kept)
      {
        BlockPos at = sites.Next();
        cases++;
        int stages = 0;
        Guard(
          world,
          block,
          "placed",
          Report,
          () =>
          {
            world.Accessor.SetBlock(block.BlockId, at, new ItemStack(block));
            Judge(world, block, at, "placed", Report);
            if (
              world
                .GetBlockEntity(at)
                ?.GetBehavior<ExRightClickConstructable>() is
              { } construction
            )
              stages = construction.Construction.Stages.Length - 1;
          }
        );
        if (!firstFacing.Contains(block))
          continue;
        for (int stage = 1; stage <= stages; stage++)
        {
          int paid = stage;
          BlockPos staged = sites.Next();
          cases++;
          Guard(
            world,
            block,
            $"stage {paid}",
            Report,
            () =>
            {
              world.Accessor.SetBlock(
                block.BlockId,
                staged,
                new ItemStack(block)
              );
              StructureBreaks.Pay(
                world,
                staged,
                block,
                world
                  .GetBlockEntity(staged)!
                  .GetBehavior<ExRightClickConstructable>(),
                paid,
                StructureBreaks.Payment.CreativeWithCtrl,
                []
              );
              Judge(world, block, staged, $"stage {paid}", Report);
            }
          );
        }
        if (BlockLaws.Signals(world, block).Layout == null)
          continue;
        BlockPos formed = sites.Next();
        Guard(
          world,
          block,
          "formed",
          Report,
          () =>
          {
            BlockLaws.EntityCase? step = MultiblockLaw.Formed(
              world,
              block,
              formed,
              registered,
              null
            );
            if (step == null)
              return;
            foreach (BlockPos cell in MultiblockLaw.FormedCells(world, step))
            {
              Block formedBlock = world.GetBlock(cell);
              if (formedBlock is BlockStructureFiller)
                continue;
              cases++;
              Judge(world, formedBlock, cell, "formed", Report);
            }
          }
        );
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static void Judge(
    TestWorld world,
    Block block,
    BlockPos at,
    string state,
    Action<string> report
  )
  {
    DrawnCell cell = DrawnCell.Of(world, at);
    string code = block.Code.ToString();
    if (cell.Threw is { } threw)
      report($"{code} {state} OnTesselation threw {Because(threw)}");
    if (cell.BlockMeshThrew is { } blockThrew)
      report($"{code} {state} block mesh threw {Because(blockThrew)}");
    if (world.GetBlockEntity(at) is { } entity)
    {
      if (Overrider(entity) is { } overrider && !Tesselated(block.DrawType))
        report(
          $"{code} overrides OnTesselation on draw type {block.DrawType}, which never calls it ({overrider})"
        );
    }
    if (block.DrawType != EnumDrawType.Empty && !Tesselated(block.DrawType))
      return;
    if (cell.DrawsNothing)
      report(
        $"{code} {state} draws nothing ("
          + (
            block.DrawType == EnumDrawType.Empty ? "draw type Empty"
            : cell.SkipsBlockMesh
              ? "OnTesselation returned true and pooled nothing"
            : "block shape has no faces"
          )
          + ")"
      );
    if (cell.VerticesOutside > 0)
      report(
        $"{code} {state} draws {cell.VerticesOutside} of {cell.Vertices} vertices outside its "
          + "footprint, "
          + (
            double.IsInfinity(cell.Overshoot)
              ? "some with a NaN coordinate"
              : $"up to {Number(cell.Overshoot)} blocks beyond"
          )
          + $", bounds {Describe(cell.Bounds!)}"
      );
  }

  private static void Guard(
    TestWorld world,
    Block block,
    string state,
    Action<string> report,
    Action act
  )
  {
    int logged = world.Log.Entries.Count;
    try
    {
      act();
    }
    catch (Exception e)
    {
      report($"{block.Code} {state} threw {BlockLaws.Describe(e)}");
    }
    foreach (
      (EnumLogType type, string message) in world
        .Log.Entries.Skip(logged)
        .ToList()
    )
    {
      world.Log.Expect(type, message);
      if (type is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
        report($"{block.Code} {state} logged {type}: {message}");
    }
  }

  private static bool Tesselated(EnumDrawType type) =>
    type
      is EnumDrawType.JSON
        or EnumDrawType.JSONAndWater
        or EnumDrawType.JSONAndSnowLayer;

  private static string? Overrider(BlockEntity entity)
  {
    if (Declares(entity.GetType(), typeof(BlockEntity)))
      return entity.GetType().Name;
    return entity
      .Behaviors.Select(b => b.GetType())
      .FirstOrDefault(t => Declares(t, typeof(BlockEntityBehavior)))
      ?.Name;
  }

  private static bool Declares(Type type, Type root) =>
    type.GetMethod(
      nameof(BlockEntity.OnTesselation),
      [typeof(ITerrainMeshPool), typeof(ITesselatorAPI)]
    )?.DeclaringType != root;

  private static string Because(Exception e)
  {
    string first = e.StackTrace?.Split('\n')[0] ?? "";
    if (e is NullReferenceException)
    {
      if (first.Contains("ShapeTesselator.TesselateShapeElements("))
        return "a shape element without from and to of 3 values (the game warns and drops it)";
      if (first.Contains("ShapeTesselator.TesselateShapeElement("))
        return "a face without 4 uv values (the game warns and drops it), or an element colour map the draw layer cannot resolve";
    }
    return $"{e.GetType().Name}: {e.Message}";
  }

  private static readonly string[] FacingGroups =
  [
    "side",
    "orientation",
    "horizontalorientation",
  ];

  private static string VariantKey(TestWorld world, Block block)
  {
    BlockSignals signals = BlockLaws.Signals(world, block);
    string facing = string.Join(
      ",",
      BlockSignals
        .OrientationGroups(block)
        .Concat(FacingGroups.Where(g => block.Variant?.ContainsKey(g) == true))
        .Distinct()
        .Select(g => $"{g}={block.Variant[g]}")
    );
    return string.Join(
      "|",
      block.GetType().FullName,
      signals.EntityClass,
      signals.EntityType?.FullName,
      string.Join(",", signals.EntityBehaviors),
      signals.Stages,
      signals.Layout != null,
      block.DrawType,
      ShapeKey(block.Shape),
      facing
    );
  }

  private static string ShapeKey(CompositeShape? shape) =>
    shape == null
      ? ""
      : string.Join(
        ",",
        shape.Base,
        shape.Format,
        shape.rotateX,
        shape.rotateY,
        shape.rotateZ,
        shape.offsetX,
        shape.offsetY,
        shape.offsetZ,
        shape.Scale,
        string.Join("/", shape.SelectiveElements ?? []),
        string.Join("/", shape.IgnoreElements ?? []),
        "[" + string.Join(";", (shape.Overlays ?? []).Select(ShapeKey)) + "]"
      );

  private static string Number(double value) =>
    value.ToString("0.##", CultureInfo.InvariantCulture);

  private static string Describe(Cuboidd bounds) =>
    $"{Number(bounds.X1)}..{Number(bounds.X2)} {Number(bounds.Y1)}..{Number(bounds.Y2)} "
    + $"{Number(bounds.Z1)}..{Number(bounds.Z2)}";
}
#endif
