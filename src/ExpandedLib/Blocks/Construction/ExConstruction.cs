// exlib's right-click construction subsystem, compiled on every game version.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Blocks;

/// <summary>One construction stage: shape elements it adds/removes and the materials it needs.</summary>
public class ExConstructionStage {
  public string[]? AddElements;
  public string[]? RemoveElements;
  public ExConstructionIngredient[]? RequireStacks;
  public string ActionLangCode = "rollers-construct";
}

/// <summary>A required material for a stage; <see cref="StoreWildCard"/> records the chosen
/// variant so later stages and drops resolve to the same one.</summary>
public class ExConstructionIngredient : CraftingRecipeIngredient {
  public string? StoreWildCard;

#if GAME_GE_1_22
  public override ExConstructionIngredient Clone() {
    ExConstructionIngredient result = new();
    CloneTo(result);
    return result;
  }

  protected override void CloneTo(object cloneTo) {
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

  public bool OnInteract(EntityAgent byEntity, ItemSlot handslot) {
    if (CurrentCompletedStage >= Stages.Length - 1)
      return false;
    if (!TryConsumeIngredients(byEntity, handslot))
      return false;
    if (CurrentCompletedStage < Stages.Length - 1)
      CurrentCompletedStage++;
    return true;
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

  private bool TryConsumeIngredients(EntityAgent byEntity, ItemSlot handslot) {
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
    bool creativeInstant =
      player.WorldData.CurrentGameMode == EnumGameMode.Creative
      && byEntity.Controls.CtrlKey;

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
  /// <returns>One interaction per required ingredient listing the stacks that satisfy it, or null
  /// when construction is complete, the stage requires nothing, or an ingredient does not
  /// resolve. The last answer is kept while the stages, the next stage and the stored variants
  /// are unchanged, and the world is scanned once per distinct filled ingredient. The arrays are
  /// shared; callers do not mutate them.</returns>
  public WorldInteraction[]? GetInteractionHelp() {
    int next = CurrentCompletedStage + 1;
    if (next >= Stages.Length)
      return null;
    if (Stages[next].RequireStacks == null)
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
    foreach (var required in stage.RequireStacks!) {
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
    if (stage.RequireStacks.Length == 0)
      list.Add(
        new WorldInteraction {
          ActionLangCode = stage.ActionLangCode,
          MouseButton = EnumMouseButton.Right,
        }
      );
    return list.ToArray();
  }
}
