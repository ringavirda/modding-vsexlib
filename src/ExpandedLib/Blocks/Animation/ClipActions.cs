using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks;

/// <summary>The clip actions one block declares in <c>attributes.clipActions</c>, as the game
/// resolved the attribute for its variant.</summary>
public sealed class ClipActions {
  private readonly string _owner;

  private ClipActions(string owner, IReadOnlyList<ClipAction> declared) {
    _owner = owner;
    Declared = declared;
  }

  /// <summary>No action.</summary>
  public static ClipActions None { get; } = new("", []);

  /// <summary>The actions as written, in declaration order, their defaults not yet filled.</summary>
  public IReadOnlyList<ClipAction> Declared { get; }

  /// <summary>Reads a block's declared actions.</summary>
  /// <param name="block">The block, loaded.</param>
  /// <returns>The actions; <see cref="None"/> when the block declares no <c>clipActions</c>.</returns>
  /// <exception cref="InvalidOperationException">An entry names no clip, has neither a
  /// <c>state</c> nor a <c>by</c> line, or is a lock.</exception>
  public static ClipActions Of(Block block) {
    JsonObject[]? entries = block.Attributes?["clipActions"]?.AsArray();
    if (entries == null)
      return None;
    string owner = block.Code.ToString();
    return new ClipActions(
      owner,
      [.. entries.Select(entry => ClipAction.Read(entry, owner))]
    );
  }

  /// <summary>The declared actions with every default filled from <paramref name="shape"/>:
  /// <see cref="ClipAction.Pose"/>, <see cref="ClipAction.Seconds"/> and
  /// <see cref="ClipAction.Parts"/>.</summary>
  /// <param name="shape">The block's shape, with its animations.</param>
  /// <returns>One action per declared one, in the same order.</returns>
  /// <exception cref="InvalidOperationException">An action names a clip the shape lacks.</exception>
  public IReadOnlyList<ClipAction> Resolve(Shape shape) =>
    [
      .. Declared.Select(action =>
      {
        Animation clip =
          shape.Animations?.FirstOrDefault(a =>
            string.Equals(
              a.Code,
              action.Clip,
              StringComparison.OrdinalIgnoreCase
            )
          )
          ?? throw new InvalidOperationException(
            $"{_owner}: clip action '{action.Clip}' names a clip the shape does not have."
          );
        return action with
        {
          Pose =
            action.Pose
            ?? (
              clip.OnAnimationEnd == EnumEntityAnimationEndHandling.Repeat
                ? ClipPose.Run
                : ClipPose.Frame
            ),
          Seconds = action.Seconds ?? (clip.QuantityFrames - 1) / 30f,
          Parts =
            action.Parts
            ??
            [
              .. clip
                .KeyFrames.SelectMany(frame => frame.Elements.Keys)
                .Distinct(),
            ],
        };
      }),
    ];
}
