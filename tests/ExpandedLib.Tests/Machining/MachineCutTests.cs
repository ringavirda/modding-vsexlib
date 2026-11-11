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
      Verdict(Job(minTier: 2), new MachineToolSpec(1, "drillbit", null, 0))
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
  public void A_1_kg_cast_iron_job_has_185800_J_of_period_energy_and_is_credited_one_second_of_game_energy() {
    float energy = MachineCut.Energy(Job(), CutMotion.Rotary, Materials());
    Assert.Equal(185800f, energy, 1);
    float torque = MachineCut.LoadTorque(Lathe, Lathe.Steps[0], true);
    Assert.Equal(82.2f, torque, 2);
    float game = MachineCut.GameEnergy(Job(), Lathe.Steps[0], Materials());
    Assert.Equal(30f * 822f, game, 1);
    Assert.Equal(
      torque * 10d * 30d,
      MachineCut.Credit(torque, 10f, 30d, game),
      3
    );
    Assert.Equal(game, MachineCut.Credit(torque, 10f, 30.1d, game), 1);
  }

  // Fails when the square root is dropped: the spur gear's drill job would take 1523 s, not 3.9 s.
  [Fact]
  public void The_spur_gears_jobs_take_3_9_s_at_the_drill_and_6_2_s_at_the_shaper() {
    const double Kg = 0.7027d;
    Assert.Equal(3.9d, MachineCut.JobSeconds(126d * Kg * 185_800d, 360f), 1);
    Assert.Equal(6.2d, MachineCut.JobSeconds(316d * Kg * 295_500d, 570f), 1);
  }

  // Fails when the hand cap is removed: 91.8 MJ at 44.7 W is 26.2 s uncapped.
  [Fact]
  public void The_spur_gears_hand_route_takes_the_24_s_cap() {
    const double Kg = 0.7027d;
    double energy = 316d * Kg * 295_500d + 126d * Kg * 295_500d;
    Assert.Equal(24d, MachineCut.HandSeconds(energy, 44.7f), 6);
    Assert.Equal(10d, MachineCut.HandSeconds(10_000d * 44.7d * 30d, 44.7f), 6);
  }

  // Fails when the floor is removed: 1000 J at 360 W is 0.058 s.
  [Fact]
  public void A_job_under_the_floor_takes_1_s_and_a_hand_route_2_s() {
    Assert.Equal(1d, MachineCut.JobSeconds(1000d, 360f), 6);
    Assert.Equal(2d, MachineCut.HandSeconds(1000d, 44.7f), 6);
  }

  // Fails when the cap is removed: 1e12 J at 360 W is 5.2e5 s.
  [Fact]
  public void A_job_over_the_cap_takes_12_s() =>
    Assert.Equal(12d, MachineCut.JobSeconds(1e12d, 360f), 6);

  [Fact]
  public void A_job_with_no_energy_or_no_power_takes_no_time() {
    Assert.Equal(0d, MachineCut.JobSeconds(0d, 360f));
    Assert.Equal(0d, MachineCut.JobSeconds(1e6d, 0f));
    Assert.Equal(0d, MachineCut.HandSeconds(0d, 44.7f));
  }

  [Fact]
  public void Half_speed_takes_twice_the_game_seconds() {
    float torque = MachineCut.LoadTorque(Lathe, Lathe.Steps[0], true);
    double full = MachineCut.Credit(torque, 10f, 1d, 1e9);
    double half = MachineCut.Credit(torque, 5f, 1d, 1e9);
    Assert.Equal(full / 2d, half, 6);
  }

  // Fails when the credited energy is not 30 x T x P: a run at half speed takes twice T real seconds.
  [Fact]
  public void A_run_at_half_the_rated_speed_takes_twice_the_job_seconds() {
    MachiningJob job = Job() with { Energy = 140_000_000f };
    MachineKindStep step = Lathe.Steps[0];
    double t = MachineCut.JobSeconds(140_000_000d, step.Power);
    float torque = MachineCut.LoadTorque(Lathe, step, true);
    float game = MachineCut.GameEnergy(job, step, Materials());
    Assert.Equal(7.5d, t, 1);
    Assert.Equal(t, game / MachineCut.Credit(torque, 10f, 30d, 1e12), 6);
    Assert.Equal(2d * t, game / MachineCut.Credit(torque, 5f, 30d, 1e12), 4);
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
  public void Hand_time_of_a_declared_energy_is_the_scaled_root_of_it_over_the_rotary_hand_power_over_30() {
    CuttingMaterialRegistry materials = Materials();
    double seconds = MachineCut.HandSeconds(
      MachineCut.Energy(
        Job() with {
          Energy = 18_300_000f,
        },
        CutMotion.Rotary,
        materials
      ),
      materials.HandPower(CutMotion.Rotary)
    );
    Assert.Equal(10d, seconds, 6);
  }
}
