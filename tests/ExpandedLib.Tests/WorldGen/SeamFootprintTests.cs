#if GAME_GE_1_22
using System;
using ExpandedLib.WorldGen;
using Xunit;

namespace ExpandedLib.Tests.WorldGen;

public class SeamFootprintTests
{
  private static DepositFootprint Footprint(
    float exponent,
    float direction = 0
  ) => new(100, 60, exponent, 0.6f, 1.0f, direction);

  // Fails when the exponent is ignored and 2 used: at n 4 s is 0.82 and the column is inside,
  // at n 2 it is 1.28 and outside.
  [Fact]
  public void A_corner_column_is_inside_a_square_outline_and_outside_an_ellipse()
  {
    Assert.True(Footprint(4).Contains(40, 24, 0));
    Assert.False(Footprint(2).Contains(40, 24, 0));
  }

  // Fails when the edge noise is not applied: noise 1 pulls the edge to 0.8, under s 0.82.
  [Fact]
  public void Edge_noise_pulls_the_outline_in()
  {
    Assert.True(Footprint(4).Contains(40, 24, 0));
    Assert.False(Footprint(4).Contains(40, 24, 1));
  }

  // Fails when the taper ramp is taken from taperFull to 1 instead of to taperZero.
  [Fact]
  public void Taper_is_one_inside_full_and_falls_to_zero()
  {
    var fp = new DepositFootprint(100, 60, 4, 0.6f, 0.9f, 0);

    Assert.Equal(1f, fp.Taper(0.5f, 1f));
    Assert.Equal(0.5f, fp.Taper(0.75f, 1f), 3);
    Assert.Equal(0f, fp.Taper(0.95f, 1f));
  }

  // Fails when the area weight s^(2/n) is replaced by s.
  [Theory]
  [InlineData(4, 0.6f, 1.0f, 0.892)]
  [InlineData(2, 0.4f, 0.9f, 0.650)]
  public void Mean_taper_weights_rings_by_area(
    float n,
    float full,
    float zero,
    double expected
  )
  {
    Assert.Equal(expected, DepositFootprint.MeanTaper(n, full, zero), 0.002);
  }

  // Fails when the edge noise mean is taken as 1 instead of 0.9.
  [Theory]
  [InlineData(4, 0.6f, 1.0f, 0.846)]
  [InlineData(2, 0.4f, 0.9f, 0.585)]
  public void Expected_factor_counts_the_noise_shrunk_outline(
    float n,
    float full,
    float zero,
    double expected
  )
  {
    Assert.Equal(
      expected,
      DepositFootprint.ExpectedFactor(n, full, zero),
      0.003
    );
  }

  // Fails when the gamma arguments 1 + 1/n and 1 + 2/n are swapped.
  [Theory]
  [InlineData(4, 0.927)]
  [InlineData(2, 0.7854)]
  public void Area_is_the_superellipse_share_of_the_bounding_box(
    float n,
    double share
  )
  {
    Assert.Equal(
      share * 100 * 60,
      DepositFootprint.Area(100, 60, n),
      0.002 * 6000
    );
  }

  // Fails when the dip is ignored in the plane.
  [Theory]
  [InlineData(0f)]
  [InlineData(MathF.PI / 2)]
  public void Base_falls_down_the_dip_and_is_level_along_strike(float direction)
  {
    var fp = Footprint(2, direction);
    float cos = MathF.Cos(direction);
    float sin = MathF.Sin(direction);
    float Y(float along, float across)
    {
      var (u, _) = fp.Locate(
        along * cos - across * sin,
        along * sin + across * cos
      );
      return fp.BaseY(100, 0.3f, u);
    }

    Assert.Equal(30f, Y(-50, 0) - Y(50, 0), 0.001);
    Assert.Equal(Y(0, -20), Y(0, 20), 0.001);
  }
}
#endif
