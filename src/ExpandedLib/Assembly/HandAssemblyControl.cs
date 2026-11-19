using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Helpers;
using ExpandedLib.Machines;
using ExpandedLib.Networks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The client's control of hand assembly: what the held part or cap and the aim offer, the press of the right button and the packet it sends. Draws nothing. Used from one thread.</summary>
/// <remarks>The game repeats a held right button's press every build repeat delay and never reports its release, so a press stays one press until a frame reads the button up: its repeats neither step again nor send another packet.</remarks>
internal sealed class HandAssemblyControl {
  private const string TakeOffKey = "takeoff";
  private const string CapKey = "cap";
  private const string FitKey = "fit";

  private readonly IClientNetworkChannel _channel;
  private readonly Action<string, string[]> _refuse;
  private readonly HandAssemblyStepper _stepper = new();

  private IReadOnlyList<AssemblyCandidate> _candidates = [];
  private BlockPos? _takeOff;
  private string _capCode = "";
  private string _fitCode = "";
  private string _target = "";
  private int _slot;
  private bool _pressed;

  /// <param name="channel">Receives the <see cref="AssemblyFix"/>, <see cref="AssemblyTakeOff"/>, <see cref="AssemblyCap"/> or <see cref="AssemblyFit"/> a click sends.</param>
  /// <param name="refuse">Shows a refusal: the placement failure code and the arguments its line formats.</param>
  public HandAssemblyControl(
    IClientNetworkChannel channel,
    Action<string, string[]> refuse
  ) {
    _channel = channel;
    _refuse = refuse;
  }

  /// <summary>The aimed block while a part is held; null otherwise.</summary>
  public BlockSelection? Aim { get; private set; }

  /// <summary>The held part when it is opted in; null otherwise.</summary>
  public Block? Part { get; private set; }

  /// <summary>The held part's assembly spec; null when no part is held.</summary>
  public HandAssemblySpec? Spec { get; private set; }

  /// <summary>The HUD line of the fixed piece the wrench in the main hand is aimed at and would take off: what a survival take-off gives besides the piece, read from the records the block entities hold.</summary>
  /// <param name="world">The world read.</param>
  /// <param name="player">The local player; creative says nothing comes back.</param>
  /// <param name="get">Translates a lang key with its arguments; <c>Lang.Get</c> in the game.</param>
  /// <returns>The line; null when no fixed piece is aimed with the wrench.</returns>
  public string? TakeOffLine(
    IWorldAccessor world,
    IPlayer? player,
    System.Func<string, object[], string> get
  ) {
    if (_takeOff == null || player == null)
      return null;
    IBlockAccessor accessor = world.BlockAccessor;
    (IReadOnlyList<ItemStack> stacks, bool anyFree) =
      HandAssemblyServer.TakeOffReturns(accessor, _takeOff);
    return HandAssemblyHud.TakeOffLine(
      get,
      accessor.GetBlock(_takeOff).GetPlacedBlockName(world, _takeOff),
      HandAssemblyServer.Grouped(stacks),
      anyFree,
      HandAssemblyGate.IsCreative(player)
    );
  }

  /// <summary>The fixed piece a cap held in the main hand is aimed at; null otherwise.</summary>
  public BlockPos? CapAt { get; private set; }

  /// <summary>The end of <see cref="CapAt"/> the held cap goes on: the arm nearest the hit among those that take a cap, or the nearest arm of all when none does; null when no cap is aimed.</summary>
  public BlockFacing? CapFace { get; private set; }

  /// <summary>The aimed piece's assembly spec while a cap is aimed; null otherwise.</summary>
  public HandAssemblySpec? CapSpec { get; private set; }

  /// <summary>The placed block the item in the main hand is aimed at and fits onto; null otherwise.</summary>
  public BlockPos? FitAt { get; private set; }

  /// <summary>The block the aimed fit would make; null when no fit is aimed.</summary>
  public Block? FitBlock { get; private set; }

  /// <summary>The aimed block's assembly spec while a fit is aimed; null otherwise.</summary>
  public HandAssemblySpec? FitSpec { get; private set; }

  /// <summary>The refusal code of <see cref="CapFace"/> when the end takes no cap (capped, joined or closed); null when it does.</summary>
  public string? CapRefusal { get; private set; }

  /// <summary>The refusal code of the aimed cell while a part is held: a cap standing in it, or the part's own reason when its refinement filtered every placement; null otherwise.</summary>
  public string? Refusal { get; private set; }

  /// <summary>Whether the tool or the fasteners for the shown placement, the aimed cap or the aimed fit are missing, or the aimed end takes no cap.</summary>
  public bool Short { get; private set; }

  /// <summary>The shown placement's position in the offer, from 0.</summary>
  public int Index => _stepper.Index;

  /// <summary>How many placements are offered.</summary>
  public int Count => _stepper.Count;

  /// <summary>The placement shown; null when none is offered.</summary>
  public AssemblyCandidate? Shown =>
    _stepper.Shown is { } code
      ? _candidates.FirstOrDefault(c => c.Block.Code.ToString() == code)
      : null;

  /// <summary>Recomputes the offer from what <paramref name="player"/> holds and aims at.</summary>
  /// <param name="world">The world read.</param>
  /// <param name="player">The local player; null clears the offer.</param>
  /// <param name="selection">The block aimed at; null for none.</param>
  public void Refresh(
    IWorldAccessor world,
    IPlayer? player,
    BlockSelection? selection
  ) {
    Part = null;
    Spec = null;
    Aim = null;
    Short = false;
    _takeOff = null;
    CapAt = null;
    CapFace = null;
    CapSpec = null;
    CapRefusal = null;
    FitAt = null;
    FitBlock = null;
    FitSpec = null;
    Refusal = null;
    _candidates = [];
    if (player?.Entity == null)
      return;

    IBlockAccessor accessor = world.BlockAccessor;
    ItemSlot main = player.InventoryManager.ActiveHotbarSlot;
    _slot = player.InventoryManager.ActiveHotbarSlotNumber;

    if (
      main.Itemstack?.Block is { } part
      && HandAssemblySpec.Of(part) is { } spec
    ) {
      Part = part;
      Spec = spec;
      if (selection != null) {
        Aim = selection;
        _candidates = HandAssemblyCandidates.Of(world, player, part, selection);
        HandAssemblyTarget target = HandAssemblyTarget.Resolve(
          accessor,
          selection,
          part
        );
        _target = $"{target.Cell}|{target.Anchor.Code}";
        if (HandAssemblyCaps.InTheWay(accessor, target.Cell) != null)
          Refusal = HandAssemblyServer.CapInWay;
        else if (_candidates.Count == 0)
          Refusal = HandAssemblyCandidates.EmptyRefusal(
            world,
            player,
            part,
            selection
          );
      }
    } else if (selection != null && AimCap(world, main, selection)) {
      _target = $"{CapKey}|{CapAt}|{CapFace!.Code}";
    } else if (selection != null && AimFit(world, main, selection)) {
      _target = $"{FitKey}|{FitAt}";
    } else if (
        selection != null
        && HandAssemblySpec.Of(accessor.GetBlock(selection.Position)) is { } aimed
        && HandAssemblyServer.HoldsTool(main, aimed.Tool)
        && IsFixed(accessor, selection.Position)
      ) {
      _takeOff = selection.Position.Copy();
      _target = $"{TakeOffKey}|{_takeOff}";
    }

    _stepper.Retarget(
      _target,
      [.. _candidates.Select(c => c.Block.Code.ToString())]
    );
    Short =
      CapFace != null ? IsCapShort(player)
      : FitAt != null ? IsFitShort(player)
      : Spec != null && Shown is { } shown && IsShort(player, shown);
  }

  /// <summary>Takes a right-button press: the first of a press steps the shown placement, or with sneak sends the fix, take-off or cap; a repeat of the same press does nothing. A press without sneak on an aimed cap is not hand assembly's.</summary>
  /// <param name="world">The world read.</param>
  /// <param name="player">The local player.</param>
  /// <param name="selection">The block aimed at; null for none.</param>
  /// <param name="sneak">Whether sneak is held.</param>
  /// <returns>Whether the press is hand assembly's, and the game's own use of it is to be skipped.</returns>
  public bool Press(
    IWorldAccessor world,
    IPlayer player,
    BlockSelection? selection,
    bool sneak
  ) {
    if (_pressed)
      return true;

    Refresh(world, player, selection);
    if (Part != null && Aim != null) {
      if (!sneak)
        _stepper.Step();
      else
        SendFix(world, player);
    } else if (FitAt != null) {
      if (!sneak && !HandAssemblyGate.IsCreative(player)) {
        if (!HandAssemblyServer.HoldsTool(player.Entity.LeftHandItemSlot, FitSpec!.Tool))
          ShowFitRefusal(world, player);
        else
          return false;
      } else
        SendFit(world, player);
    } else if (sneak && _takeOff != null)
      Send();
    else if (sneak && CapFace != null)
      SendCap(world, player);
    else
      return false;
    _pressed = true;
    return true;
  }

  /// <summary>Reads the button for a frame: the button up ends the press, so the next press acts again.</summary>
  /// <param name="down">Whether the right button is down now.</param>
  public void Frame(bool down) {
    if (!down)
      _pressed = false;
  }

  /// <summary>Ends the press.</summary>
  public void Release() => _pressed = false;

  private void SendFix(IWorldAccessor world, IPlayer player) {
    if (Refusal != null) {
      _refuse(Refusal, []);
      return;
    }
    if (Shown is not { } shown || Spec == null)
      return;
    if (Short) {
      ShowRefusal(world, player, Spec, shown);
      return;
    }
    Send();
  }

  private bool AimFit(
    IWorldAccessor world,
    ItemSlot main,
    BlockSelection selection
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    BlockPos pos = selection.Position;
    if (
      main.Itemstack?.Collectible == null
      || HandFitAim.Of(accessor, pos, main.Itemstack, out _) is not { } aim
      || HandAssemblySpec.Of(accessor.GetBlock(pos)) is not { } spec
    )
      return false;
    FitAt = pos.Copy();
    FitBlock = aim.Result;
    FitSpec = spec;
    _fitCode = main.Itemstack.Collectible.Code.ToString();
    return true;
  }

  private void SendFit(IWorldAccessor world, IPlayer player) {
    if (Short) {
      ShowFitRefusal(world, player);
      return;
    }
    Send();
  }

  private void ShowFitRefusal(IWorldAccessor world, IPlayer player) {
    HandAssemblySpec spec = FitSpec!;
    if (
      !HandAssemblyServer.HoldsTool(player.Entity.LeftHandItemSlot, spec.Tool)
    )
      _refuse(
        HandAssemblyServer.NeedsTool,
        [HandAssemblyServer.ToolNoun(world, spec.Tool)]
      );
    else
      _refuse(
        HandAssemblyServer.NeedsFasteners,
        [
          spec.PerFit.ToString(),
          HandAssemblyServer.FastenerName(world, spec.Fastener),
          ExCarried.Count(player, spec.Fastener).ToString(),
        ]
      );
  }

  private bool IsFitShort(IPlayer player) {
    if (HandAssemblyGate.IsCreative(player))
      return false;
    HandAssemblySpec spec = FitSpec!;
    return !HandAssemblyServer.HoldsTool(
        player.Entity.LeftHandItemSlot,
        spec.Tool
      )
      || ExCarried.Count(player, spec.Fastener) < spec.PerFit;
  }

  private bool AimCap(
    IWorldAccessor world,
    ItemSlot main,
    BlockSelection selection
  ) {
    IBlockAccessor accessor = world.BlockAccessor;
    BlockPos pos = selection.Position;
    if (
      main.Itemstack?.Item == null
      || accessor.GetBlock(pos) is not BlockNetworkNode node
      || HandAssemblySpec.Of(node) is not { Cap: { } cap } spec
      || !HandAssemblyServer.HoldsTool(main, cap)
      || BEBehaviorAssembled.At(accessor, pos) is not { Fixed: true } record
    )
      return false;
    Vec3d hit = selection.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
    BlockFacing? face = HandAssemblyCaps.NearestArm(accessor, node, pos, hit);
    if (face == null)
      return false;
    CapAt = pos.Copy();
    CapFace = face;
    CapSpec = spec;
    CapRefusal = HandAssemblyCaps.FitRefusal(world, node, pos, record, face);
    _capCode = main.Itemstack.Collectible.Code.ToString();
    return true;
  }

  private void SendCap(IWorldAccessor world, IPlayer player) {
    if (Short) {
      ShowCapRefusal(world, player);
      return;
    }
    Send();
  }

  private void ShowCapRefusal(IWorldAccessor world, IPlayer player) {
    HandAssemblySpec spec = CapSpec!;
    if (CapRefusal != null)
      _refuse(CapRefusal, []);
    else if (
      !HandAssemblyServer.HoldsTool(player.Entity.LeftHandItemSlot, spec.Tool)
    )
      _refuse(
        HandAssemblyServer.NeedsTool,
        [HandAssemblyServer.ToolNoun(world, spec.Tool)]
      );
    else
      _refuse(
        HandAssemblyServer.NeedsFasteners,
        [
          spec.PerJoint.ToString(),
          HandAssemblyServer.FastenerName(world, spec.Fastener),
          ExCarried.Count(player, spec.Fastener).ToString(),
        ]
      );
  }

  private bool IsCapShort(IPlayer player) {
    if (CapRefusal != null)
      return true;
    if (HandAssemblyGate.IsCreative(player))
      return false;
    HandAssemblySpec spec = CapSpec!;
    return !HandAssemblyServer.HoldsTool(
        player.Entity.LeftHandItemSlot,
        spec.Tool
      )
      || ExCarried.Count(player, spec.Fastener) < spec.PerJoint;
  }

  private void ShowRefusal(
    IWorldAccessor world,
    IPlayer player,
    HandAssemblySpec spec,
    AssemblyCandidate shown
  ) {
    if (
      !HandAssemblyServer.HoldsTool(player.Entity.LeftHandItemSlot, spec.Tool)
    )
      _refuse(
        HandAssemblyServer.NeedsTool,
        [HandAssemblyServer.ToolNoun(world, spec.Tool)]
      );
    else
      _refuse(
        HandAssemblyServer.NeedsFasteners,
        [
          (shown.Joints * spec.PerJoint).ToString(),
          HandAssemblyServer.FastenerName(world, spec.Fastener),
          ExCarried.Count(player, spec.Fastener).ToString(),
        ]
      );
  }

  private static bool IsFixed(IBlockAccessor world, BlockPos pos) =>
    world.GetBlock(pos) is not BlockNetworkNode
    || BEBehaviorAssembled.At(world, pos) is { Fixed: true };

  private bool IsShort(IPlayer player, AssemblyCandidate shown) {
    if (HandAssemblyGate.IsCreative(player))
      return false;
    HandAssemblySpec spec = Spec!;
    return !HandAssemblyServer.HoldsTool(
        player.Entity.LeftHandItemSlot,
        spec.Tool
      )
      || ExCarried.Count(player, spec.Fastener) < shown.Joints * spec.PerJoint;
  }

  private void Send() {
    if (_takeOff != null) {
      _channel.SendPacket(
        new AssemblyTakeOff {
          X = _takeOff.X,
          Y = _takeOff.Y,
          Z = _takeOff.Z,
          Dimension = _takeOff.dimension,
        }
      );
      return;
    }
    if (FitAt != null) {
      _channel.SendPacket(
        new AssemblyFit {
          X = FitAt.X,
          Y = FitAt.Y,
          Z = FitAt.Z,
          Dimension = FitAt.dimension,
          Slot = _slot,
          ItemCode = _fitCode,
        }
      );
      return;
    }
    if (CapAt != null && CapFace != null) {
      _channel.SendPacket(
        new AssemblyCap {
          X = CapAt.X,
          Y = CapAt.Y,
          Z = CapAt.Z,
          Dimension = CapAt.dimension,
          Face = CapFace.Index,
          Slot = _slot,
          CapCode = _capCode,
        }
      );
      return;
    }
    if (Shown is not { } shown || Aim == null || Part == null)
      return;
    _channel.SendPacket(
      new AssemblyFix {
        X = Aim.Position.X,
        Y = Aim.Position.Y,
        Z = Aim.Position.Z,
        Dimension = Aim.Position.dimension,
        Face = Aim.Face.Index,
        HitX = Aim.HitPosition.X,
        HitY = Aim.HitPosition.Y,
        HitZ = Aim.HitPosition.Z,
        Slot = _slot,
        PartCode = Part.Code.ToString(),
        CandidateCode = shown.Block.Code.ToString(),
      }
    );
  }
}
