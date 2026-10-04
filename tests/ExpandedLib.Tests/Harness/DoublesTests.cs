using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The convenience doubles wired into <see cref="TestWorld"/>: a player with a real
/// hotbar, a recording logger, a mod loader, a config-file round-trip and the world-config
/// tree.</summary>
public class DoublesTests {
  private sealed class FakeConfig {
    public int Value { get; set; }
    public string Name { get; set; } = "";
  }

  #region TestPlayer

  [Fact]
  public void Held_stack_reads_back_through_the_players_active_hotbar_slot() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();
    var stack = new ItemStack(
      new Item { Code = new AssetLocation("game:pick-iron") }
    );

    player.Hold(stack);

    Assert.Same(
      stack,
      player.Player.InventoryManager.ActiveHotbarSlot.Itemstack
    );
  }

  [Fact]
  public void Sneaking_is_reflected_in_the_entitys_own_controls() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();

    player.Sneaking = true;

    Assert.True(player.Entity.Controls.Sneak);
    Assert.True(player.Sneaking);
  }

  // Fails when CtrlHeld writes anything but the entity's own CtrlKey control.
  [Fact]
  public void Ctrl_is_reflected_in_the_entitys_own_controls() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();

    player.CtrlHeld = true;

    Assert.True(player.Entity.Controls.CtrlKey);
    Assert.True(player.CtrlHeld);
  }

  // Fails when GameMode is not what the player's world data answers.
  [Fact]
  public void The_game_mode_reads_back_through_the_players_world_data() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();

    player.GameMode = EnumGameMode.Creative;

    Assert.Equal(
      EnumGameMode.Creative,
      player.Player.WorldData.CurrentGameMode
    );
  }

  // Fails when the world does not answer the player's uid with the player, or the inventory manager
  // hands out another hotbar: the game reaches both through the entity.
  [Fact]
  public void The_entity_resolves_to_the_player_and_its_hotbar() {
    using var world = new TestWorld();
    TestPlayer player = world.Player("paying");

    Assert.Same(player.Player, player.Entity.Player);
    Assert.Same(
      player.Hotbar,
      player.Entity.Player.InventoryManager.GetHotbarInventory()
    );
    Assert.Equal(12, player.Hotbar.Count);
  }

  // Fails when the manager does not answer the engine's cursor id with Mouse: the slot protocol
  // reads the cursor as GetInventory("mouse-" + WorldData.PlayerUID)[0].
  [Fact]
  public void The_cursor_answers_under_the_engines_id() {
    using var world = new TestWorld();
    TestPlayer player = world.Player("paying");
    IPlayerInventoryManager manager = player.Player.InventoryManager;

    Assert.Same(
      player.Mouse,
      manager.GetInventory("mouse-" + player.Player.WorldData.PlayerUID)
    );
    Assert.Same(player.Mouse[0], manager.MouseItemSlot);
    Assert.Same(player.Hotbar, manager.GetOwnInventory("hotbar"));
  }

  // Fails when OpenInventory does not hold the inventory and run its Open, or CloseInventory does
  // not run its Close.
  [Fact]
  public void Opening_holds_the_inventory_and_closing_lets_it_go() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();
    InventoryGeneric chest = TestInventory.Of(world, 4, "chest-1");
    IPlayerInventoryManager manager = player.Player.InventoryManager;

    manager.OpenInventory(chest);

    Assert.True(chest.HasOpened(player.Player));
    Assert.Same(chest, manager.GetInventory("chest-1"));
    Assert.Contains(chest, manager.OpenedInventories);
    Assert.True(manager.GetInventory("chest-1", out InventoryBase? found));
    Assert.Same(chest, found);

    manager.CloseInventory(chest);

    Assert.False(chest.HasOpened(player.Player));
    Assert.DoesNotContain(chest, manager.OpenedInventories);
  }

  // Fails when the world's class registry hands an inventory a substitute network util: a slot
  // packet would then move nothing.
  [Fact]
  public void An_inventory_carries_the_engines_network_util() {
    using var world = new TestWorld();
    InventoryGeneric chest = TestInventory.Of(world, 4, "chest-1");

    Assert.Equal(
      "Vintagestory.Common.InventoryNetworkUtil",
      chest.InvNetworkUtil.GetType().FullName
    );
  }

  // Fails when the entity's right hand is not the active slot: vanilla reads a held tool there.
  [Fact]
  public void The_entitys_right_hand_is_the_active_slot() {
    using var world = new TestWorld();
    TestPlayer player = world.Player();

    Assert.Same(player.ActiveSlot, player.Entity.RightHandItemSlot);
  }

  // Fails when the world's Collectibles leaves out its registered items or blocks.
  [Fact]
  public void The_worlds_collectibles_hold_its_items_and_blocks() {
    using var world = new TestWorld();
    Item item = world.RegisterItem("game:stick");
    Block block = TestBlocks.Configure(new Block(), "game:plank-oak", 5002);
    world.Register(block);

    Assert.Contains(item, world.World.Collectibles);
    Assert.Contains(block, world.World.Collectibles);
  }

  // Fails when a wildcard search answers nothing, or answers what the wildcard does not match.
  [Fact]
  public void A_wildcard_search_finds_the_registered_items_and_blocks_it_matches() {
    using var world = new TestWorld();
    Item iron = world.RegisterItem("game:metalplate-iron");
    world.RegisterItem("game:stick");
    Block oak = TestBlocks.Configure(new Block(), "game:plank-oak", 5002);
    world.Register(oak);
    world.Register(TestBlocks.Configure(new Block(), "game:log-oak", 5003));

    Assert.Equal(
      [iron],
      world.World.SearchItems(new AssetLocation("game:metalplate-*"))
    );
    Assert.Equal(
      [oak],
      world.World.SearchBlocks(new AssetLocation("game:plank-*"))
    );
  }

  // Fails when a block registered without sounds keeps none: vanilla plays a block's placement
  // sound unguarded, as the engine's registration never leaves it null.
  [Fact]
  public void A_block_registered_without_sounds_gets_empty_ones() {
    using var world = new TestWorld();
    Block bare = TestBlocks.Configure(new Block(), "game:bare", 5004);
    bare.Sounds = null!;

    world.Register(bare);

    Assert.NotNull(bare.Sounds);
  }

  // Fails when the api's object cache is left a substitute's default: vanilla collectibles cache
  // their stacks and meshes there from OnLoaded on.
  [Fact]
  public void The_api_object_cache_keeps_what_is_put_in_it() {
    using var world = new TestWorld();

    world.Api.ObjectCache["key"] = 5;

    Assert.Equal(5, world.Api.ObjectCache["key"]);
  }

  #endregion

  #region ModConfig

  [Fact]
  public void A_config_class_round_trips_through_store_and_load() {
    using var world = new TestWorld();
    var config = new FakeConfig { Value = 42, Name = "furnace" };

    world.Api.StoreModConfig(config, "fake.json");
    FakeConfig? loaded = world.Api.LoadModConfig<FakeConfig>("fake.json");

    Assert.NotNull(loaded);
    Assert.Equal(config.Value, loaded!.Value);
    Assert.Equal(config.Name, loaded.Name);
  }

  [Fact]
  public void LoadModConfig_answers_null_for_a_file_never_stored() {
    using var world = new TestWorld();

    Assert.Null(world.Api.LoadModConfig<FakeConfig>("never-written.json"));
  }

  #endregion

  #region TestModLoader

  [Fact]
  public void IsModEnabled_and_every_alias_report_every_id_enabled() {
    using var world = new TestWorld();

    Assert.True(world.Api.ModLoader.IsModEnabled("exlib"));
    Assert.True(world.Mods.IsModLoaded("exlib"));
    Assert.True(world.Mods.HasMod("exlib"));
    Assert.True(world.Mods.HasModId("exlib"));

    Assert.True(world.Api.ModLoader.IsModEnabled("nonexistent"));
    Assert.True(world.Mods.IsModLoaded("nonexistent"));
  }

  [Fact]
  public void A_mod_added_disabled_is_absent_from_GetMod_but_still_reports_enabled() {
    using var world = new TestWorld();

    world.Mods.Add("offmod", "1.0.0", enabled: false);

    Assert.True(world.Api.ModLoader.IsModEnabled("offmod"));
    Assert.Null(world.Api.ModLoader.GetMod("offmod"));
  }

  #endregion

  #region RecordingLogger

  [Fact]
  public void A_logged_error_is_retrievable_through_worldLog() {
    using var world = new TestWorld();
    world.Log.Expect(EnumLogType.Error, "the furnace");

    world.Api.Logger.Error("Something went wrong at {0}", "the furnace");

    Assert.Contains(world.Log.Errors, m => m.Contains("the furnace"));
  }

  #endregion

  #region WorldConfigBag

  [Fact]
  public void World_config_is_readable_and_writable_through_the_api() {
    using var world = new TestWorld();

    world.Api.World.Config.SetString("some-key", "some-value");

    Assert.Equal("some-value", world.Api.World.Config.GetString("some-key"));
    Assert.Same(world.Config.Tree, world.Api.World.Config);
  }

  #endregion
}
