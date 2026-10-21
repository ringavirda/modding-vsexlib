using ExpandedLib.Industry.MechanicalPower;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Cam-pulsed load arithmetic: mean torque, step-averaged torque and drops crossed.</summary>
public class MpCamLoadTests {
  private static readonly MpCamLoad Helve = new(3400f, 3, 1.047f);

  [Fact]
  public void Mean_torque_is_energy_times_lifts_over_a_turn() {
    Assert.Equal(1623f, Helve.MeanTorque(), 1f);
  }

  [Fact]
  public void Step_torque_over_one_turn_pays_every_lift_at_any_phase_offset() {
    const float speed = 2f;
    const float dt = 0.2f;
    foreach (float start in new[] { 0f, 0.37f }) {
      double paid = 0;
      float phase = start;
      // 200 ms steps until one full turn is covered, the last one trimmed.
      double remaining = 2 * System.Math.PI;
      while (remaining > 1e-9) {
        float step = (float)System.Math.Min(speed * dt, remaining);
        paid += Helve.StepTorque(phase, speed, step / speed) * step;
        phase += step;
        remaining -= step;
      }
      Assert.InRange(paid, 3 * 3400.0 * 0.999, 3 * 3400.0 * 1.001);
    }
  }

  [Fact]
  public void Step_torque_inside_a_lift_is_the_peak_and_between_lifts_is_zero() {
    Assert.Equal(3400f / 1.047f, Helve.StepTorque(0.1f, 2f, 0.1f), 1f);
    Assert.Equal(0f, Helve.StepTorque(1.2f, 2f, 0.1f), 3);
  }

  [Fact]
  public void Drops_crossed_counts_every_drop_in_a_long_step() {
    Assert.Equal(2, Helve.DropsCrossed(0.5f, 4.5f));
    Assert.Equal(0, Helve.DropsCrossed(0.5f, 0.4f));
    Assert.Equal(1, Helve.DropsCrossed(1.0f, 0.1f));
    Assert.Equal(0, Helve.DropsCrossed(1.0f, 0f));
  }
}
