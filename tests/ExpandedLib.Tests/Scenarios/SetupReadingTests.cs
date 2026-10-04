using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios;

/// <summary>Each reading form of setups.md, Readings, read off a <see cref="TestWorld"/>.</summary>
public class SetupReadingTests {
  private static BlockPos Cell => new(0, 0, 0);

  private static BlockPos Empty => new(5, 0, 0);

  #region Fixtures

  private enum Mode {
    Idle,
    Firing,
  }

  private sealed class Gauge {
#pragma warning disable CS0414
    private int _level = 7;
#pragma warning restore CS0414
  }

  private sealed class Draught(BlockEntity be) : BlockEntityBehavior(be) {
#pragma warning disable CS0414
    private float _pull = 0.5f;
#pragma warning restore CS0414
  }

  private sealed class Boiler : BlockEntity {
    private readonly Gauge _gauge = new();

    public float Heat = 3f;

    public Mode State = Mode.Firing;

    public Boiler() => Behaviors.Add(new Draught(this));

    private float Doubled(IWorldAccessor world) =>
      world == null ? -1f : Heat * 2f;

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetFloat("heat", Heat);
    }
  }

  private sealed class FlowState {
    public float Pressure { get; set; } = 2.5f;
  }

  private static class Limits {
    private static int _reads;

    public static int Reads => ++_reads;
  }

  /// <summary>A world holding a <see cref="Boiler"/> at <see cref="Cell"/>, placed as
  /// <c>test:boiler-north</c>.</summary>
  private static (SetupTestWorld World, Boiler Boiler) World() {
    var world = new SetupTestWorld();
    var boiler = new Boiler();
    world.Test.Place(
      Cell,
      TestBlocks.Configure(new Block(), "test:boiler-north", 1),
      boiler
    );
    return (world, boiler);
  }

  private static Reading Parse(string json, bool inRecord = true) =>
    SetupReadings.Parse(
      JToken.Parse(json),
      new SetupJson.Where("test.json", "steps[0] (wait).until"),
      new ReadingScope(null, inRecord, InCheck: false, new SetupReferences())
    );

  private static object Read(string json, SetupTestWorld world) =>
    Parse(json).Read(new ReadingContext(world));

  #endregion

  #region World

  // Fails when a non-public field is skipped, a dotted path stops at its first segment, or an
  // int or an enum is not read as a float or as its member's name.
  [Fact]
  public void A_member_reads_non_public_fields_down_a_dotted_path() {
    var (world, _) = World();

    Assert.Equal(7f, Read("""{ "at": [0, 0, 0], "member": "_gauge._level" }""", world));
    Assert.Equal("Firing", Read("""{ "at": [0, 0, 0], "member": "State" }""", world));
  }

  // Fails when a first segment naming a behaviour's class is read on the block entity.
  [Fact]
  public void A_member_whose_first_segment_names_a_behaviour_reads_the_behaviour() {
    var (world, _) = World();

    Assert.Equal(0.5f, Read("""{ "at": [0, 0, 0], "member": "Draught._pull" }""", world));
  }

  // Fails when a method segment is not called, or is called without the world.
  [Fact]
  public void A_member_ending_in_parentheses_calls_the_method_with_the_world() {
    var (world, _) = World();

    Assert.Equal(6f, Read("""{ "at": [0, 0, 0], "member": "Doubled()" }""", world));
  }

  // Fails when "or" is ignored for an empty cell or a type lacking the member, or a missing
  // member without "or" reads as anything.
  [Fact]
  public void A_member_with_or_reads_or_where_the_cell_or_its_type_lacks_it() {
    var (world, _) = World();

    Assert.Equal(0f, Read("""{ "at": [5, 0, 0], "member": "Heat", "or": 0 }""", world));
    Assert.Equal(-1f, Read("""{ "at": [0, 0, 0], "member": "Cold", "or": -1 }""", world));
    Assert.Throws<SetupReadingException>(() =>
      Read("""{ "at": [0, 0, 0], "member": "Cold" }""", world)
    );
  }

  // Fails when a tree key is read off another tree than ToTreeAttributes', or "or" is ignored.
  [Fact]
  public void A_tree_reads_a_key_of_the_saved_tree() {
    var (world, boiler) = World();
    boiler.Heat = 4.5f;

    Assert.Equal(4.5f, Read("""{ "at": [0, 0, 0], "tree": "heat" }""", world));
    Assert.Equal(0f, Read("""{ "at": [0, 0, 0], "tree": "cold", "or": 0 }""", world));
  }

  // Fails when a block reading ignores its wildcard or the cell, or reads air as no block.
  [Fact]
  public void A_block_reads_whether_the_cell_matches_the_wildcard() {
    var (world, _) = World();

    Assert.Equal(true, Read("""{ "at": [0, 0, 0], "block": "test:boiler-*" }""", world));
    Assert.Equal(false, Read("""{ "at": [0, 0, 0], "block": "test:pump-*" }""", world));
    Assert.Equal(true, Read("""{ "at": [5, 0, 0], "block": "game:air" }""", world));
    Assert.Equal(false, Read("""{ "at": [5, 0, 0], "block": "test:*" }""", world));
  }

  // Fails when a bare network reading ignores the cell or the type.
  [Fact]
  public void A_bare_network_reads_whether_the_cell_is_a_node_of_that_type() {
    var world = new SetupTestWorld();
    world.Test.RegisterNetwork("test", s => new StubNetwork(s));
    world.Test.PlaceNode(Cell, "test", "ns");

    Assert.Equal(true, Read("""{ "at": [0, 0, 0], "network": "test" }""", world));
    Assert.Equal(false, Read("""{ "at": [0, 0, 0], "network": "pipe" }""", world));
    Assert.Equal(false, Read("""{ "at": [5, 0, 0], "network": "test" }""", world));
  }

  // Fails when a network member skips the state, skips the network, or "or" is ignored.
  [Fact]
  public void A_network_member_reads_the_state_before_the_network() {
    var world = new SetupTestWorld();
    world.Test.RegisterNetwork("test", s => new StubNetwork(s));
    world.Test.PlaceNode(Cell, "test", "ns");
    var network = (StubNetwork)world.Test.NetworkAt(Cell)!;
    network.RestoreState(new FlowState());
    network.Tag = "hot";

    Assert.Equal(2.5f, Read("""{ "at": [0, 0, 0], "network": "test", "member": "Pressure" }""", world));
    Assert.Equal("hot", Read("""{ "at": [0, 0, 0], "network": "test", "member": "Tag" }""", world));
    Assert.Equal(
      0f,
      Read("""{ "at": [5, 0, 0], "network": "test", "member": "Pressure", "or": 0 }""", world)
    );
  }

  // Fails when an "of" position reads its plain cell, or ignores what the id placed.
  [Fact]
  public void An_of_position_reads_the_cell_its_id_placed() {
    var (world, _) = World();
    world.Laid("boiler", Cell);

    Assert.Equal(3f, Read("""{ "at": { "of": "boiler" }, "member": "Heat" }""", world));
    Assert.Throws<SetupFileException>(() =>
      Read("""{ "at": { "of": "pump" }, "member": "Heat" }""", world)
    );
  }

  #endregion

  #region Values

  // Fails when a static is looked up by another name than its type's full name and member, or
  // is read again after the first read.
  [Fact]
  public void A_static_reads_the_member_once() {
    Reading limit = Parse("""{ "static": "ExpandedLib.Scenarios.SetupRecording.MaxBytes" }""");
    Reading reads = Parse(
      """{ "static": "ExpandedLib.Tests.Scenarios.SetupReadingTests+Limits.Reads" }"""
    );
    var ctx = new ReadingContext(null);

    Assert.Equal((float)SetupRecording.MaxBytes, limit.Read(ctx));
    object first = reads.Read(ctx);
    Assert.Equal(first, reads.Read(ctx));
  }

  // Fails when a literal is read as another kind, or a number is not a float.
  [Fact]
  public void A_value_reads_its_literal() {
    var ctx = new ReadingContext(null);

    Assert.Equal(2.5f, Parse("""{ "value": 2.5 }""").Read(ctx));
    Assert.Equal(3f, Parse("3").Read(ctx));
    Assert.Equal("shut", Parse("\"shut\"").Read(ctx));
    Assert.Equal(true, Parse("true").Read(ctx));
  }

  // Fails when a sum drops a term.
  [Fact]
  public void A_sum_adds_its_readings() {
    Assert.Equal(6f, Parse("""{ "sum": [1, 2, { "value": 3 }] }""").Read(new ReadingContext(null)));
  }

  // Fails when a product starts from 0 or drops a term.
  [Fact]
  public void A_product_multiplies_its_readings() {
    Assert.Equal(24f, Parse("""{ "product": [2, 3, 4] }""").Read(new ReadingContext(null)));
  }

  // Fails when a quotient divides the second reading by the first.
  [Fact]
  public void A_quotient_divides_the_first_reading_by_the_second() {
    Assert.Equal(1.5f, Parse("""{ "quotient": [6, 4] }""").Read(new ReadingContext(null)));
  }

  // Fails when a quotient by 0 reads as infinity instead of failing.
  [Fact]
  public void A_quotient_by_zero_fails() {
    Reading q = Parse("""{ "quotient": [6, 0] }""");

    Assert.Throws<SetupReadingException>(() => q.Read(new ReadingContext(null)));
  }

  // Fails when abs keeps the sign.
  [Fact]
  public void An_abs_reads_the_magnitude() {
    Assert.Equal(2f, Parse("""{ "abs": -2 }""").Read(new ReadingContext(null)));
  }

  // Fails when a when reads then while a test fails, or else is not 0 by default.
  [Fact]
  public void A_when_reads_then_while_every_test_holds_else_zero() {
    var (world, boiler) = World();
    Reading when = Parse(
      """
      { "when": [{ "at": [0, 0, 0], "member": "Heat", "max": 4 }, { "value": 1, "min": 0 }],
        "then": 9 }
      """
    );
    var ctx = new ReadingContext(world);

    Assert.Equal(9f, when.Read(ctx));
    boiler.Heat = 5f;
    Assert.Equal(0f, when.Read(ctx));
  }

  // Fails when words skips its rules, reads the last entry first, or ignores "lower".
  [Fact]
  public void Words_read_the_first_holding_rule_else_the_last_entry() {
    var (world, boiler) = World();
    Reading words = Parse(
      """
      { "words": [
        { "when": { "at": [0, 0, 0], "member": "Heat", "min": 5 }, "word": "hot" },
        { "when": { "at": [0, 0, 0], "member": "Heat", "min": 4 }, "word": "warm" },
        { "at": [0, 0, 0], "member": "State", "lower": true }
      ] }
      """
    );
    var ctx = new ReadingContext(world);

    Assert.Equal("firing", words.Read(ctx));
    boiler.Heat = 6f;
    Assert.Equal("hot", words.Read(ctx));
  }

  #endregion

  #region Record only

  // Fails when a delta's baseline is not the reading after the build, or a second read twice
  // reads its change twice.
  [Fact]
  public void A_delta_reads_the_change_since_the_last_sample() {
    var (world, boiler) = World();
    Reading delta = Parse("""{ "delta": { "at": [0, 0, 0], "member": "Heat" } }""");
    var ctx = new ReadingContext(world) { Phase = ReadingPhase.Start };
    delta.Start(ctx);
    ctx.Phase = ReadingPhase.After;

    boiler.Heat = 5f;
    ctx.Second = 0;
    Assert.Equal(2f, delta.Read(ctx));
    Assert.Equal(2f, delta.Read(ctx));
    boiler.Heat = 4f;
    ctx.Second = 1;
    Assert.Equal(-1f, delta.Read(ctx));
  }

  // Fails when a rise below its last value reads the negative change instead of the count.
  [Fact]
  public void A_rise_below_its_last_value_reads_as_itself() {
    var (world, boiler) = World();
    Reading rise = Parse("""{ "rise": { "at": [0, 0, 0], "member": "Heat" } }""");
    var ctx = new ReadingContext(world) { Phase = ReadingPhase.Start };
    rise.Start(ctx);
    ctx.Phase = ReadingPhase.After;

    boiler.Heat = 8f;
    ctx.Second = 0;
    Assert.Equal(5f, rise.Read(ctx));
    boiler.Heat = 2f;
    ctx.Second = 1;
    Assert.Equal(2f, rise.Read(ctx));
  }

  // Fails when before reads the value after the ticks.
  [Fact]
  public void A_before_reads_the_value_taken_before_the_ticks() {
    var (world, boiler) = World();
    Reading before = Parse("""{ "before": { "at": [0, 0, 0], "member": "Heat" } }""");
    var ctx = new ReadingContext(world) { Phase = ReadingPhase.Before, Second = 0 };
    before.Before(ctx);
    boiler.Heat = 9f;
    ctx.Phase = ReadingPhase.After;

    Assert.Equal(3f, before.Read(ctx));
  }

  // Fails when a before not read before this second's ticks reads the last second's value.
  [Fact]
  public void A_before_not_read_before_the_second_fails() {
    var (world, _) = World();
    Reading before = Parse("""{ "before": { "at": [0, 0, 0], "member": "Heat" } }""");
    var ctx = new ReadingContext(world) { Phase = ReadingPhase.Before, Second = 0 };
    before.Before(ctx);
    ctx.Phase = ReadingPhase.After;
    ctx.Second = 1;

    Assert.Throws<SetupReadingException>(() => before.Read(ctx));
  }

  // Fails when delta, rise or before parse outside record.
  [Theory]
  [InlineData("delta")]
  [InlineData("rise")]
  [InlineData("before")]
  public void A_record_only_form_outside_record_is_refused(string form) {
    var e = Assert.Throws<SetupFileException>(() =>
      Parse($$"""{ "{{form}}": 1 }""", inRecord: false)
    );

    Assert.Equal($"test.json: steps[0] (wait).until: \"{form}\" is read in record only.", e.Message);
  }

  // Fails when a reading naming two forms reads one of them.
  [Fact]
  public void A_reading_naming_two_forms_is_refused() {
    var e = Assert.Throws<SetupFileException>(() =>
      Parse("""{ "value": 1, "abs": 2 }""")
    );

    Assert.Contains("names two reading forms", e.Message);
  }

  #endregion
}
