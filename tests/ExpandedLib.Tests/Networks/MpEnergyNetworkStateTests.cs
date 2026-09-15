using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Mechanical-energy shaft math: one shaft integrated as torque on inertia
/// (<c>w += (tau_drive - tau_load - tau_fric)/I * dt</c>), with <c>E = 1/2Iw^2</c> for the readout.
/// </summary>
public class MpEnergyNetworkStateTests {
  private const float MaxSpeed = 2f;
  private const float Wide = 1000f; // speed ceiling set high enough never to clamp
  #region Capacity / speed relationship

  [Fact]
  public void Capacity_is_the_energy_of_a_flywheel_at_max_speed() {
    // E_cap = 1/2*I*w_max^2: a full reservoir is a flywheel at the burst speed.
    Assert.Equal(
      0.5f * 10f * MaxSpeed * MaxSpeed,
      MpEnergyNetworkState.CapacityFor(10f, MaxSpeed),
      3
    );
  }

  [Fact]
  public void Speed_and_energy_round_trip_through_the_flywheel_law() {
    // w = sqrt(2E/I) is the inverse of E = 1/2Iw^2.
    float e = MpEnergyNetworkState.EnergyAtSpeed(8f, 1.5f);
    Assert.Equal(1.5f, MpEnergyNetworkState.DeriveSpeed(e, 8f), 3);
  }

  [Fact]
  public void No_inertia_means_no_speed() {
    // No inertia: nothing spins, whatever the energy figure says.
    Assert.Equal(0f, MpEnergyNetworkState.DeriveSpeed(100f, 0f));
  }

  #endregion

  #region Shaft dynamics

  [Fact]
  public void Drive_torque_spins_the_shaft_up_and_stores_energy() {
    var s = new MpEnergyNetworkState { Inertia = 10f };
    // dw = tau/I*dt = 5/10*1 = 0.5; E = 1/2*10*0.5^2.
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 5f,
      loadTorque: 0f,
      frictionCoeff: 0f,
      idleTorque: 0f,
      MaxSpeed
    );

    Assert.Equal(0.5f, s.Speed, 3);
    Assert.Equal(
      MpEnergyNetworkState.EnergyAtSpeed(10f, 0.5f),
      s.StoredEnergy,
      3
    );
  }

  [Fact]
  public void A_charged_shaft_coasts_down_under_friction_with_no_drive() {
    var s = new MpEnergyNetworkState { Inertia = 10f, Speed = 1f };
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 0f,
      loadTorque: 0f,
      frictionCoeff: 0.5f,
      idleTorque: 0f,
      MaxSpeed
    );

    Assert.True(s.Speed < 1f, "an undriven spinning shaft should wind down"); // 1 - 0.5·1/10
  }

  [Fact]
  public void Speed_clamps_to_the_burst_ceiling() {
    var s = new MpEnergyNetworkState { Inertia = 1f };
    // A torque this large overshoots in one step; the clamp holds w <= w_max.
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 1000f,
      loadTorque: 0f,
      frictionCoeff: 0f,
      idleTorque: 0f,
      MaxSpeed
    );

    Assert.Equal(MaxSpeed, s.Speed, 3);
  }

  [Fact]
  public void Steady_state_holds_speed_when_drive_balances_load_and_friction() {
    var s = new MpEnergyNetworkState { Inertia = 10f, Speed = 2f };
    // tau_fric = 0.5*2 = 1; drive 2 vs load 1 balances at dw = 0.
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 2f,
      loadTorque: 1f,
      frictionCoeff: 0.5f,
      idleTorque: 0f,
      Wide
    );

    Assert.Equal(2f, s.Speed, 3);
  }

  #endregion

  #region The flywheel is inertia, not a battery

  [Fact]
  public void A_drive_below_the_resistance_floor_never_starts_the_shaft() {
    // A drive below load + idle at rest leaves w and stored energy at 0.
    var s = new MpEnergyNetworkState { Inertia = 10f };
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 0.4f,
      loadTorque: 0f,
      frictionCoeff: 0f,
      idleTorque: 0.5f,
      Wide
    );

    Assert.Equal(0f, s.Speed);
    Assert.Equal(0f, s.StoredEnergy);
  }

  [Fact]
  public void An_over_load_drags_the_shaft_to_a_hard_stall() {
    // A load the drive cannot cover winds w down; a large enough one stalls the shaft outright.
    var s = new MpEnergyNetworkState { Inertia = 1f, Speed = 2f };
    MpEnergyNetworkState.Step(
      s,
      1f,
      driveTorque: 0f,
      loadTorque: 5f,
      frictionCoeff: 0f,
      idleTorque: 0f,
      Wide
    );

    Assert.Equal(0f, s.Speed);
    Assert.Equal(0f, s.StoredEnergy);
  }

  [Fact]
  public void A_heavy_flywheel_barely_dents_under_a_load_spike() {
    // The same load pulse on a light and a heavy shaft; inertia buffers the speed drop.
    var light = new MpEnergyNetworkState { Inertia = 1f, Speed = 5f };
    var heavy = new MpEnergyNetworkState { Inertia = 100f, Speed = 5f };

    MpEnergyNetworkState.Step(light, 0.1f, 0f, loadTorque: 10f, 0f, 0f, Wide);
    MpEnergyNetworkState.Step(heavy, 0.1f, 0f, loadTorque: 10f, 0f, 0f, Wide);

    Assert.True(
      5f - heavy.Speed < 5f - light.Speed,
      $"heavy dω {5f - heavy.Speed} should be far smaller than light dω {5f - light.Speed}"
    );
  }

  #endregion

  #region Gear coupling (the transmission)

  private static float TotalKE(
    MpEnergyNetworkState a,
    MpEnergyNetworkState b
  ) =>
    MpEnergyNetworkState.EnergyAtSpeed(a.Inertia, a.Speed)
    + MpEnergyNetworkState.EnergyAtSpeed(b.Inertia, b.Speed);

  [Fact]
  public void Coupling_holds_the_reduction_constraint() {
    // South is the small gear; a ratio of 2 holds the north at half the south's speed.
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 2f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 0f };

    MpEnergyNetworkState.CoupleRatio(south, north, ratio: 2f, Wide);

    Assert.Equal(south.Speed / 2f, north.Speed, 4);
    Assert.True(
      north.Speed < south.Speed,
      "S→N is a reduction, so the north side turns slower"
    );
  }

  [Fact]
  public void Lossless_coupling_conserves_total_energy() {
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 2f };
    var north = new MpEnergyNetworkState { Inertia = 5f, Speed = 0.3f };
    float before = TotalKE(south, north);

    MpEnergyNetworkState.CoupleRatio(
      south,
      north,
      ratio: 4f,
      Wide,
      retention: 1f
    );

    Assert.Equal(before, TotalKE(south, north), 3);
    Assert.Equal(south.Speed / 4f, north.Speed, 4);
  }

  [Fact]
  public void A_consistent_pair_is_left_untouched() {
    // Already on the constraint (w_north = w_south / r); the projection is a no-op.
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 2f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 1f };

    MpEnergyNetworkState.CoupleRatio(south, north, ratio: 2f, Wide);

    Assert.Equal(2f, south.Speed, 4);
    Assert.Equal(1f, north.Speed, 4);
  }

  [Fact]
  public void Driving_from_the_north_speeds_the_south_up() {
    // Reverse power flow: the north (big gear) drives the south (small gear).
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 0f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 1f };

    MpEnergyNetworkState.CoupleRatio(south, north, ratio: 2f, Wide);

    Assert.True(south.Speed > north.Speed, "N→S speeds up");
    Assert.Equal(south.Speed / 2f, north.Speed, 4);
  }

  [Fact]
  public void Mesh_loss_drains_a_little_energy() {
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 2f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 0f };
    float before = TotalKE(south, north);

    MpEnergyNetworkState.CoupleRatio(
      south,
      north,
      ratio: 2f,
      Wide,
      retention: 0.9f
    );

    Assert.Equal(before * 0.9f, TotalKE(south, north), 3);
  }

  [Fact]
  public void Coupling_a_side_with_no_inertia_is_a_no_op() {
    // A side with no inertia is left alone by the coupling.
    var south = new MpEnergyNetworkState { Inertia = 0f, Speed = 0f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 1f };

    MpEnergyNetworkState.CoupleRatio(south, north, ratio: 2f, Wide);

    Assert.Equal(1f, north.Speed, 4);
    Assert.Equal(0f, south.Speed, 4);
  }

  [Fact]
  public void Over_energised_coupling_clamps_the_south_and_keeps_the_ratio() {
    // Above the ceiling the south clamps to w_max; the north follows at w_max / r.
    var south = new MpEnergyNetworkState { Inertia = 10f, Speed = 10f };
    var north = new MpEnergyNetworkState { Inertia = 10f, Speed = 0f };

    MpEnergyNetworkState.CoupleRatio(south, north, ratio: 2f, MaxSpeed);

    Assert.Equal(MaxSpeed, south.Speed, 4);
    Assert.Equal(MaxSpeed / 2f, north.Speed, 4);
  }

  #endregion
}
