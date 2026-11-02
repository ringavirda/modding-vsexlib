using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Machining;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The machine tool's cut arithmetic: verdicts, load, progress and the hand time.</summary>
public class MachineCutTests {
  private static readonly MachineKind Lathe = new(
    "test:lathe",
    10f,
    0f,
    false,
    [],
    [new MachineKindStep("turn", CutMotion.Rotary, 822f, "cutter", null, null)]
  );

  private static readonly MachineToolSpec Cutter = new(2, "cutter", null, 0);

  private static MachiningJob Job(
    int minTier = 0,
    string? fixture = null,
    string[]? machines = null
  ) {
    MachiningJob job = MachiningTestData.Job(
      "turn",
      "test:blank",
      "test:turned",
      material: "castiron"
    ) with {
      MinTier = minTier,
      Fixture = fixture,
      Machines = machines ?? [],
    };
    job.RemovedKg = 1f;
    job.ResolvedMaterial = "castiron";
    return job;
  }

  private static CuttingMaterialRegistry Materials() =>
    MachiningTestData.MaterialRegistry(MachiningTestData.Materials);

  private static MachineCutVerdict Verdict(
    MachiningJob? job = null,
    MachineToolSpec? tool = null,
    bool fixture = true,
    bool chips = true,
    float speed = 10f,
    bool noTool = false
  ) =>
    MachineCut.Verdict(
      job ?? Job(),
      Lathe,
      noTool ? null : tool ?? Cutter,
      fixture,
      chips,
      speed
    );

  [Fact]
  public void Ready_when_nothing_rejects() =>
    Assert.Equal(MachineCutVerdict.Ready, Verdict());

  [Fact]
  public void No_job_for_a_missing_job() =>
    Assert.Equal(
      MachineCutVerdict.NoJob,
      MachineCut.Verdict(null, Lathe, Cutter, true, true, 10f)
    );

  [Fact]
  public void No_job_for_a_step_the_kind_lacks() =>
    Assert.Equal(
      MachineCutVerdict.NoJob,
      MachineCut.Verdict(
        Job() with {
          Step = "bore",
        },
        Lathe,
        Cutter,
        true,
        true,
        10f
      )
    );

  [Fact]
  public void No_job_for_a_job_limited_to_other_kinds() =>
    Assert.Equal(
      MachineCutVerdict.NoJob,
      Verdict(Job(machines: ["test:planer"]))
    );

  [Fact]
  public void No_tool() =>
    Assert.Equal(MachineCutVerdict.NoTool, Verdict(noTool: true));

  [Fact]
  public void Wrong_tool_kind() =>
    Assert.Equal(
      MachineCutVerdict.WrongTool,
      Verdict(tool: new MachineToolSpec(2, "drillbit", null, 0))
    );

  [Fact]
  public void Tier_short_only_for_a_tool_of_the_right_kind() =>
    Assert.Equal(
      MachineCutVerdict.TierShort,
      Verdict(Job(minTier: 2), new MachineToolSpec(1, "cutter", null, 0))
    );

  [Fact]
  public void No_fixture_only_when_the_job_names_one() {
    Assert.Equal(MachineCutVerdict.Ready, Verdict(fixture: false));
    Assert.Equal(
      MachineCutVerdict.NoFixture,
      Verdict(Job(fixture: "test:chuck"), fixture: false)
    );
  }

  [Fact]
  public void Chips_full() =>
    Assert.Equal(MachineCutVerdict.ChipsFull, Verdict(chips: false));

  [Fact]
  public void No_drive_at_rest() =>
    Assert.Equal(MachineCutVerdict.NoDrive, Verdict(speed: 0f));

  [Fact]
  public void A_1_kg_cast_iron_job_takes_185800_J_and_226_game_seconds_at_rated_speed() {
    float energy = MachineCut.Energy(Job(), CutMotion.Rotary, Materials());
    Assert.Equal(185800f, energy, 1);
    float torque = MachineCut.LoadTorque(Lathe, Lathe.Steps[0], true);
    Assert.Equal(82.2f, torque, 2);
    Assert.Equal(
      torque * 10d * 226d,
      MachineCut.Credit(torque, 10f, 226d, energy),
      3
    );
    Assert.Equal(energy, MachineCut.Credit(torque, 10f, 226.1d, energy), 1);
  }

  [Fact]
  public void Half_speed_takes_twice_the_game_seconds() {
    float torque = MachineCut.LoadTorque(Lathe, Lathe.Steps[0], true);
    double full = MachineCut.Credit(torque, 10f, 1d, 1e9);
    double half = MachineCut.Credit(torque, 5f, 1d, 1e9);
    Assert.Equal(full / 2d, half, 6);
    Assert.Equal(2d * 185800d / full, 185800d / half, 6);
  }

  [Fact]
  public void Torque_is_zero_idle() =>
    Assert.Equal(0f, MachineCut.LoadTorque(Lathe, Lathe.Steps[0], false));

  [Fact]
  public void A_stopped_run_credits_nothing() =>
    Assert.Equal(0d, MachineCut.Credit(82.2f, 0f, 100d, 185800d));

  [Fact]
  public void A_credit_past_the_remainder_is_clamped() =>
    Assert.Equal(1000d, MachineCut.Credit(82.2f, 10f, 1e6, 1000d));

  [Fact]
  public void A_declared_energy_wins_over_the_derived_one() =>
    Assert.Equal(
      500f,
      MachineCut.Energy(
        Job() with {
          Energy = 500f,
        },
        CutMotion.Rotary,
        Materials()
      )
    );

  [Fact]
  public void Hand_time_of_the_1_kg_job_is_its_energy_over_the_rotary_hand_power_over_30() {
    CuttingMaterialRegistry materials = Materials();
    double seconds = MachineCut.HandSeconds(
      MachineCut.Energy(Job(), CutMotion.Rotary, materials),
      materials.HandPower(CutMotion.Rotary)
    );
    Assert.Equal(185800d / 60d / 30d, seconds, 6);
  }
}
