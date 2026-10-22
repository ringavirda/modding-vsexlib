// exlib's right-click construction subsystem, compiled on every game version.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>One construction stage: shape elements it adds/removes and the materials it needs, and
/// optionally the tool it is worked with.</summary>
public class ExConstructionStage {
  /// <summary>The hits a tool stage takes when it names none.</summary>
  public const int DefaultHits = 2;

  public string[]? AddElements;
  public string[]? RemoveElements;
  public ExConstructionIngredient[]? RequireStacks;
  public string ActionLangCode = "rollers-construct";

  /// <summary>Item code the main-hand stack must match, wildcards allowed (<c>game:hammer-*</c>); null
  /// for a stage paid by a plain right-click.</summary>
  public string? Tool;

  /// <summary>Finished swings of <see cref="Tool"/> the stage takes, 1 or more. Ignored without a
  /// tool.</summary>
  public int Hits = DefaultHits;

  /// <summary>Entity animation code played at each swing; null for the tool's
  /// <see cref="CraftToolDefault"/>.</summary>
  public string? Animation;

  /// <summary>Asset path of the sound played at each strike; null for the tool's
  /// <see cref="CraftToolDefault"/>.</summary>
  public string? Sound;

  internal CraftStep? Step =>
    Tool == null
      ? null
      : new CraftStep(
        Tool,
        null,
        0f,
        Animation,
        null,
        null,
        Sound,
        Math.Max(1, Hits)
      );
}

/// <summary>A required material for a stage; <see cref="StoreWildCard"/> records the chosen
/// variant so later stages and drops resolve to the same one.</summary>
public class ExConstructionIngredient : CraftingRecipeIngredient {
  public string? StoreWildCard;

#if GAME_GE_1_22
  public override ExConstructionIngredient Clone()
  {
    ExConstructionIngredient result = new();
    CloneTo(result);
    return result;
  }

  protected override void CloneTo(object cloneTo)
  {
    base.CloneTo(cloneTo);
    if (cloneTo is ExConstructionIngredient ingredient)
      ingredient.StoreWildCard = StoreWildCard;
  }

  internal bool MatchesByPattern => MatchingType != EnumRecipeMatchType.Exact;
#else
  // Base Clone() is non-virtual in the legacy API; hidden here with a derived-typed clone.
  public new ExConstructionIngredient Clone() {
    var c = CloneTo<ExConstructionIngredient>();
    c.StoreWildCard = StoreWildCard;
    return c;
  }

  internal bool MatchesByPattern => IsWildCard;
#endif
}

/// <summary>What one step of a tool stage's hold did.</summary>
public enum StageHoldProgress {
  /// <summary>No hold is in progress, or it ended: the tool left the main hand, the materials are gone,
  /// or a client counted the last hit.</summary>
  Idle,

  /// <summary>The swings continue.</summary>
  Working,

  /// <summary>The last hit paid the stage and it is built.</summary>
  Built,
}

/// <summary>The per-block construction state and logic behind <see cref="ExRightClickConstructable"/>:
/// which stage is built, which variants the paid stages stored, and the stage payment.</summary>
public class ExRightClickConstruction {
  public ExConstructionStage[] Stages = Array.Empty<ExConstructionStage>();
  public int CurrentCompletedStage;
  public Dictionary<string, string> StoredWildCards = new();

  private ICoreAPI api = null!;
  private string codeForErrorLogging = "";
  private Func<Vec3d>? positionForSound;

  /// <summary>Binds the stage table and the api; a paid stage then plays no sound.</summary>
  /// <param name="stages">The stages, or null for none.</param>
  /// <param name="api">The api whose world resolves ingredients.</param>
  /// <param name="codeForErrorLogging">Names this construction in resolve warnings.</param>
  public void LateInit(
    ExConstructionStage[]? stages,
    ICoreAPI api,
    string codeForErrorLogging
  ) => LateInit(stages, api, null, codeForErrorLogging);

  /// <summary>Binds the stage table and the api; a paid stage plays the sound of the first
  /// material taken at <paramref name="positionForSound"/>.</summary>
  /// <param name="stages">The stages, or null for none.</param>
  /// <param name="api">The api whose world resolves ingredients.</param>
  /// <param name="positionForSound">The world position a paid stage's sound plays at; null plays
  /// none.</param>
  /// <param name="codeForErrorLogging">Names this construction in resolve warnings.</param>
  public void LateInit(
    ExConstructionStage[]? stages,
    ICoreAPI api,
    Func<Vec3d>? positionForSound,
    string codeForErrorLogging
  ) {
    Stages = stages ?? Array.Empty<ExConstructionStage>();
    this.api = api;
    this.codeForErrorLogging = codeForErrorLogging;
    this.positionForSound = positionForSound;
  }

  private readonly ConstructionHold hold = new();
  private bool holding;

  /// <summary>Whether the next stage is paid by holding a tool: it names one and the player is not
  /// a creative player holding Ctrl.</summary>
  /// <param name="byEntity">The player's entity.</param>
  public bool NeedsTool(EntityAgent byEntity) =>
    CurrentCompletedStage < Stages.Length - 1
    && Stages[CurrentCompletedStage + 1].Tool != null
    && !CreativeInstant(byEntity);

  private static bool CreativeInstant(EntityAgent byEntity) =>
    (byEntity as EntityPlayer)?.Player is { } player
    && player.WorldData.CurrentGameMode == EnumGameMode.Creative
    && byEntity.Controls.CtrlKey;

  /// <summary>Pays the next stage by a plain right-click.</summary>
  /// <returns>False when construction is complete, the stage needs a tool (see
  /// <see cref="NeedsTool"/>) or its materials are not in the hotbar.</returns>
  public bool OnInteract(EntityAgent byEntity, ItemSlot handslot) {
    if (CurrentCompletedStage >= Stages.Length - 1 || NeedsTool(byEntity))
      return false;
    if (!TryConsumeIngredients(byEntity, handslot))
      return false;
    if (CurrentCompletedStage < Stages.Length - 1)
      CurrentCompletedStage++;
    return true;
  }

  /// <summary>Whether a hold begun by <see cref="BeginHold"/> is in progress.</summary>
  public bool Holding => holding;

  /// <summary>Starts paying the next stage with its tool. On a client a refusal shows the player the
  /// tool or the material that is missing; on the server it shows nothing.</summary>
  /// <param name="byEntity">The player's entity; its main hand must hold the stage's tool and its hotbar
  /// the stage's materials.</param>
  /// <returns>False, and no hold, when the stage names no tool, the main hand holds another stack, or
  /// the hotbar lacks a material. Hits counted on this stage by an earlier hold stay.</returns>
  public bool BeginHold(EntityAgent byEntity) {
    if (!NeedsTool(byEntity))
      return false;
    int index = CurrentCompletedStage + 1;
    ExConstructionStage stage = Stages[index];
    ItemSlot hand = byEntity.RightHandItemSlot;
    if (!HoldsTool(stage, hand)) {
      RefuseTool(byEntity, stage);
      return false;
    }
    if (!TryConsumeIngredients(byEntity, hand, commit: false))
      return false;
    hold.Begin(index);
    holding = true;
    return true;
  }

  /// <summary>Carries the hold <see cref="BeginHold"/> began to <paramref name="secondsUsed"/>. The
  /// server counts a hit at each finished swing of the stage's animation and plays its strike sound;
  /// the hit that reaches the stage's count takes the materials from the hotbar, wears the tool as a
  /// craft step does and builds the stage. A client swings the animation alone.</summary>
  /// <param name="secondsUsed">Seconds since the hold began, as the game passes them.</param>
  /// <param name="byEntity">The player's entity.</param>
  /// <returns><see cref="StageHoldProgress.Idle"/> when no hold is in progress, the main hand no longer
  /// holds the tool or the materials are gone; hits counted stay in the first two cases.</returns>
  public StageHoldProgress StepHold(float secondsUsed, EntityAgent byEntity) {
    if (!holding || CurrentCompletedStage >= Stages.Length - 1)
      return StageHoldProgress.Idle;
    ExConstructionStage stage = Stages[CurrentCompletedStage + 1];
    ItemSlot hand = byEntity.RightHandItemSlot;
    bool client = api.Side == EnumAppSide.Client;
    ICraftEffects effects = client
      ? new AnimationOnly(new EntityCraftEffects(byEntity))
      : new EntityCraftEffects(byEntity);
    if (!HoldsTool(stage, hand)) {
      StopHold(byEntity);
      return StageHoldProgress.Idle;
    }

    CraftToolDefault? tool = CraftSequenceRegistry.Shared.ToolDefault(
      hand.Itemstack.Collectible.Code.ToString()
    );
    if (!hold.Advance(secondsUsed, stage.Step!, tool, effects))
      return StageHoldProgress.Working;

    holding = false;
    hold.Clear(effects);
    if (client)
      return StageHoldProgress.Idle;
    if (!TryConsumeIngredients(byEntity, hand))
      return StageHoldProgress.Idle;
    CraftToolWear.Apply(hand, api.World.Rand);
    CurrentCompletedStage++;
    return StageHoldProgress.Built;
  }

  /// <summary>Ends the hold the player let go of: the swing in progress stops and the hits counted
  /// stay. Does nothing when no hold is in progress.</summary>
  /// <param name="byEntity">The player's entity.</param>
  public void StopHold(EntityAgent byEntity) {
    if (!holding)
      return;
    holding = false;
    hold.Release(new EntityCraftEffects(byEntity));
  }

  private static bool HoldsTool(ExConstructionStage stage, ItemSlot hand) =>
    hand.Itemstack is { } held
    && WildcardUtil.Match(stage.Tool, held.Collectible.Code.ToString());

  private List<CollectibleObject> ToolCollectibles(string pattern) {
    var tools = new List<CollectibleObject>();
    foreach (CollectibleObject collectible in api.World.Collectibles)
      if (
        collectible?.Code != null
        && WildcardUtil.Match(pattern, collectible.Code.ToString())
      )
        tools.Add(collectible);
    return tools;
  }

  private void RefuseTool(EntityAgent byEntity, ExConstructionStage stage) {
    if (
      (byEntity as EntityPlayer)?.Player is not IClientPlayer
      || api is not ICoreClientAPI capi
    )
      return;
    List<CollectibleObject> tools = ToolCollectibles(stage.Tool!);
    string names =
      tools.Count == 0
        ? stage.Tool!
        : string.Join(
          ", ",
          tools.Take(3).Select(t => new ItemStack(t).GetName())
        ) + (tools.Count > 3 ? ", ..." : "");
    capi.TriggerIngameError(
      this,
      "missingtool",
      Lang.Get("exlib:ingameerror-construction-missingtool", names)
    );
  }

  private sealed class AnimationOnly(ICraftEffects inner) : ICraftEffects {
    public void StartAnimation(string animation) =>
      inner.StartAnimation(animation);

    public void StopAnimation(string animation) =>
      inner.StopAnimation(animation);

    public void Strike(string sound) { }
  }

  /// <summary>The shape elements completed so far, as "elem/*" selectors for the tesselator.</summary>
  public string[] getShapeElements() {
    var set = new HashSet<string>();
    for (int i = 0; i <= CurrentCompletedStage; i++) {
      var stage = Stages[i];
      if (stage.AddElements != null)
        foreach (var e in stage.AddElements)
          set.Add(e + "/*");
      if (stage.RemoveElements != null)
        foreach (var e in stage.RemoveElements)
          set.Remove(e + "/*");
    }
    return new List<string>(set).ToArray();
  }

  /// <summary>The materials of every completed stage, inclusive of the last built, each ingredient
  /// resolved with the stored variants and its stack size scaled by <paramref name="dropRatio"/>
  /// and rounded at random.</summary>
  /// <param name="dropRatio">The fraction of each stack returned, 0..1.</param>
  /// <param name="rnd">The rounding source; null rounds without randomness.</param>
  /// <returns>The stacks, empty below stage 1. An ingredient that does not resolve is
  /// left out.</returns>
  [MethodImpl(MethodImplOptions.NoInlining)]
  public ItemStack[] GetDrops(float dropRatio = 1f, Random? rnd = null) {
    if (CurrentCompletedStage < 1)
      return Array.Empty<ItemStack>();

    var list = new List<ItemStack>();
    for (int i = 0; i <= CurrentCompletedStage; i++) {
      var stage = Stages[i];
      if (stage.RequireStacks == null)
        continue;
      foreach (var ingredient in stage.RequireStacks) {
        var resolved = ingredient.Clone();
        foreach (var wc in StoredWildCards)
          resolved.FillPlaceHolder(wc.Key, wc.Value);
        if (resolved.StoreWildCard != null)
          resolved.Code.Path = resolved.Code.Path.Replace(
            "*",
            StoredWildCards[resolved.StoreWildCard]
          );

        if (
          resolved.Resolve(
            api.World,
            $"Drop stack for construction stage {i} on {codeForErrorLogging}"
          )
        ) {
          var stack = resolved.ResolvedItemStack.Clone();
          stack.StackSize = GameMath.RoundRandom(
            rnd,
            stack.StackSize * dropRatio
          );
          list.Add(stack);
        }
      }
    }
    return list.ToArray();
  }

  private bool TryConsumeIngredients(
    EntityAgent byEntity,
    ItemSlot handslot,
    bool commit = true
  ) {
    var player = (byEntity as EntityPlayer)?.Player;
    if (player == null)
      return false;

    var stage = Stages[CurrentCompletedStage + 1];
    if (stage.RequireStacks == null)
      return true;

    var hotbar = player.InventoryManager.GetHotbarInventory();
    var toTake = new List<KeyValuePair<ItemSlot, int>>();
    var remaining = new List<ExConstructionIngredient>();
    foreach (var ing in stage.RequireStacks)
      remaining.Add(ing.Clone());

    var newWildcards = new Dictionary<string, string>();
    bool creativeInstant = CreativeInstant(byEntity);

    foreach (var ing in remaining) {
      foreach (var wc in StoredWildCards)
        ing.FillPlaceHolder(wc.Key, wc.Value);
      if (
        !ing.Resolve(
          api.World,
          $"Require stack for construction stage on {codeForErrorLogging}"
        ) && !ing.MatchesByPattern
      )
        return false;
    }

    foreach (var slot in hotbar) {
      if (slot.Empty)
        continue;
      if (remaining.Count == 0)
        break;
      for (int k = 0; k < remaining.Count; k++) {
        var ing = remaining[k];
        if (
          !creativeInstant && ing.SatisfiesAsIngredient(slot.Itemstack, false)
        ) {
          int num = Math.Min(ing.Quantity, slot.Itemstack.StackSize);
          toTake.Add(new KeyValuePair<ItemSlot, int>(slot, num));
          ing.Quantity -= num;
          if (ing.Quantity <= 0) {
            remaining.RemoveAt(k);
            k--;
            if (ing.StoreWildCard != null)
              newWildcards[ing.StoreWildCard] = slot.Itemstack
                .Collectible
                .Variant[ing.StoreWildCard];
          }
        } else if (creativeInstant && ing.StoreWildCard != null) {
          newWildcards["wood"] = "oak";
          newWildcards["metal"] = "iron";
        }
      }
    }

    if (!creativeInstant && remaining.Count > 0) {
      var ing = remaining[0];
      if (player is IClientPlayer && player.Entity.Api is ICoreClientAPI capi)
        capi.TriggerIngameError(
          this,
          "missingstack",
          Lang.Get(
            "ingameerror-missingstack",
            ing.Quantity,
            ing.MatchesByPattern
              ? Lang.Get(ing.Name ?? "")
              : ing.ResolvedItemStack.GetName()
          )
        );
      return false;
    }

    if (!commit)
      return true;

    foreach (var wc in newWildcards)
      StoredWildCards[wc.Key] = wc.Value;

    if (!creativeInstant) {
      bool soundPlayed = false;
      foreach (var take in toTake) {
        if (
          !soundPlayed
          && PlaceSound(take.Key.Itemstack) is { } sound
          && positionForSound?.Invoke() is { } at
        ) {
          soundPlayed = true;
          api.World.PlaySoundAt(sound, at.X, at.Y, at.Z, player);
        }
        take.Key.TakeOut(take.Value);
        take.Key.MarkDirty();
      }
    }
    return true;
  }

  private static AssetLocation? PlaceSound(ItemStack stack) {
    AssetLocation? sound = null;
    if (stack.Block != null)
#if GAME_GE_1_22
      sound = stack.Block.Sounds?.Place.Location;
#else
      sound = stack.Block.Sounds?.Place;
#endif
    return sound
      ?? stack
        .Collectible.GetBehavior<CollectibleBehaviorGroundStorable>()
        ?.StorageProps?.PlaceRemoveSound?.WithPathPrefixOnce("sounds/");
  }

  public void ToTreeAttributes(ITreeAttribute tree) {
    var wildcards = new TreeAttribute();
    foreach (var wc in StoredWildCards)
      wildcards[wc.Key] = new StringAttribute(wc.Value);
    tree["wildcards"] = wildcards;
    tree.SetInt("currentStage", CurrentCompletedStage);
  }

  public void FromTreeAttributes(ITreeAttribute tree) {
    StoredWildCards.Clear();
    if (tree["wildcards"] is TreeAttribute wildcards)
      foreach (var entry in wildcards)
        if (entry.Value is StringAttribute s)
          StoredWildCards[entry.Key] = s.value;
    CurrentCompletedStage = tree.GetInt("currentStage", 0);
  }

  private WorldInteraction[]? hint;
  private ExConstructionStage[]? hintStages;
  private int hintNextStage = -1;
  private readonly Dictionary<string, string> hintWildCards = new();

  /// <summary>The build-material hover help for the next stage.</summary>
  /// <returns>One interaction per required ingredient listing the stacks that satisfy it, a single
  /// stack-less interaction when the stage requires nothing, or null when construction is
  /// complete or an ingredient does not resolve. The last answer is kept while the stages, the next stage and the stored variants
  /// are unchanged, and the world is scanned once per distinct filled ingredient. The arrays are
  /// shared; callers do not mutate them.</returns>
  public WorldInteraction[]? GetInteractionHelp() {
    int next = CurrentCompletedStage + 1;
    if (next >= Stages.Length)
      return null;
    if (
      hintStages == Stages
      && hintNextStage == next
      && SameWildCards(hintWildCards, StoredWildCards)
    )
      return hint;

    hint = BuildInteractionHelp(Stages[next]);
    hintStages = Stages;
    hintNextStage = next;
    hintWildCards.Clear();
    foreach (var wc in StoredWildCards)
      hintWildCards[wc.Key] = wc.Value;
    return hint;
  }

  private static bool SameWildCards(
    Dictionary<string, string> a,
    Dictionary<string, string> b
  ) {
    if (a.Count != b.Count)
      return false;
    foreach (var wc in b)
      if (!a.TryGetValue(wc.Key, out var value) || value != wc.Value)
        return false;
    return true;
  }

  private WorldInteraction[]? BuildInteractionHelp(ExConstructionStage stage) {
    var list = new List<WorldInteraction>();
    if (stage.Tool != null) {
      var tools = ToolCollectibles(stage.Tool)
        .Select(t => new ItemStack(t))
        .ToArray();
      list.Add(
        new WorldInteraction {
          ActionLangCode = "exlib:rcc-hold-tool",
          Itemstacks = tools,
          GetMatchingStacks = (wi, bs, es) => tools,
          MouseButton = EnumMouseButton.Right,
        }
      );
    }
    foreach (var required in stage.RequireStacks ?? []) {
      var ingredient = required.Clone();
      foreach (var wc in StoredWildCards)
        ingredient.FillPlaceHolder(wc.Key, wc.Value);
      if (
        !ingredient.Resolve(
          api.World,
          $"Interaction help on {codeForErrorLogging}"
        )
      )
        return null;

      var collectibles = ConstructionHints.Matching(api.World, ingredient);
      var matching = new ItemStack[collectibles.Length];
      for (int i = 0; i < matching.Length; i++)
        matching[i] = new ItemStack(collectibles[i], ingredient.Quantity);
      list.Add(
        new WorldInteraction {
          ActionLangCode = stage.ActionLangCode,
          Itemstacks = matching,
          GetMatchingStacks = (wi, bs, es) => matching,
          MouseButton = EnumMouseButton.Right,
        }
      );
    }
    if (list.Count == 0)
      list.Add(
        new WorldInteraction {
          ActionLangCode = stage.ActionLangCode,
          MouseButton = EnumMouseButton.Right,
        }
      );
    return list.ToArray();
  }
}
