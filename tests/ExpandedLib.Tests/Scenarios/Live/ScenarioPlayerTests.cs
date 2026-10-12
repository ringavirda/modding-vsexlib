using System;
using System.Collections.Generic;
using ExpandedLib.Testing.Live;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The scenario player's checks and park cell (scenarios.md, The scenario player).</summary>
public class ScenarioPlayerTests {
  /// <summary>Fails when a value that holds is reported as failing (S1).</summary>
  [Fact]
  public void A_check_that_holds_reads_through_with_no_failure() {
    bool held = PlayerCheck.Read(
      "PlayerByUid",
      () => 7,
      v => v == 7,
      out int value,
      out string? failure
    );
    Assert.True(held);
    Assert.Equal(7, value);
    Assert.Null(failure);
  }

  /// <summary>Fails when a value that does not hold passes (S2), or its line drops what was read (S3).</summary>
  [Fact]
  public void A_check_that_does_not_hold_names_what_it_read() {
    bool held = PlayerCheck.Read(
      "CurrentGameMode",
      () => EnumGameMode.Creative,
      m => m == EnumGameMode.Survival,
      out _,
      out string? failure
    );
    Assert.False(held);
    Assert.Equal("CurrentGameMode: read Creative", failure);
  }

  /// <summary>Fails when a throw escapes the check (S4), or its line drops the exception's message.</summary>
  [Fact]
  public void A_check_that_throws_names_the_exception() {
    bool held = PlayerCheck.Read<int>(
      "ServerMain.Systems",
      () => throw new InvalidOperationException("no field"),
      v => true,
      out _,
      out string? failure
    );
    Assert.False(held);
    Assert.Equal(
      "ServerMain.Systems: threw InvalidOperationException: no field",
      failure
    );
  }

  /// <summary>Fails when the park cell is not 16 blocks above the row (S5).</summary>
  [Fact]
  public void The_park_cell_sits_sixteen_blocks_above_the_row() {
    BlockPos park = ScenarioPlayer.ParkCell(new BlockPos(100, 50, -7));
    Assert.Equal((100, 66, -7), (park.X, park.Y, park.Z));
  }

  /// <summary>Fails when a build that throws is not undone (S56), or its failure drops the
  /// exception's message (S57).</summary>
  [Fact]
  public void A_build_that_throws_is_undone_and_names_the_exception() {
    bool undone = false;
    string? made = PlayerCheck.Assemble<string>(
      () => throw new InvalidOperationException("boom"),
      () => undone = true,
      out string? failure
    );
    Assert.Null(made);
    Assert.True(undone);
    Assert.Equal("build: threw InvalidOperationException: boom", failure);
  }

  /// <summary>Fails when a build that returns is undone (S59).</summary>
  [Fact]
  public void A_build_that_returns_is_not_undone() {
    bool undone = false;
    string? made = PlayerCheck.Assemble(
      () => "built",
      () => undone = true,
      out string? failure
    );
    Assert.Equal("built", made);
    Assert.False(undone);
    Assert.Null(failure);
  }

  /// <summary>Fails when the player's uid stays in a map it is removed from (S58), or when another
  /// key goes with it.</summary>
  [Fact]
  public void Forget_removes_the_uid_from_every_map_and_no_other_key() {
    var players = new Dictionary<string, int> {
      [ScenarioPlayer.Uid] = 1,
      ["other"] = 2,
    };
    var worlds = new Dictionary<string, int> { [ScenarioPlayer.Uid] = 3 };
    ScenarioPlayer.Forget(ScenarioPlayer.Uid, players, worlds);
    Assert.False(players.ContainsKey(ScenarioPlayer.Uid));
    Assert.Equal(2, players["other"]);
    Assert.False(worlds.ContainsKey(ScenarioPlayer.Uid));
  }
}
