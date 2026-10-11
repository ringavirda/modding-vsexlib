using ExpandedLib.Testing.Live;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Scenarios.Live;

/// <summary>The fixed English lines extools parses (scenarios.md, Output).</summary>
public class ScenarioOutputTests {
  /// <summary>Fails when the budget is not rounded up (V1).</summary>
  [Fact]
  public void The_run_line_rounds_the_budget_up_to_whole_seconds() =>
    Assert.Equal(
      "[scenario] run: 2 scenario(s), 3 case(s), budget 13 s, game 1.22.7",
      ScenarioOutput.Run(2, 3, 12.2, "1.22.7")
    );

  [Fact]
  public void The_case_line_gives_the_absolute_plot_origin() =>
    Assert.Equal(
      "[scenario] case exlib/sample plot 100 50 -7",
      ScenarioOutput.Case("exlib/sample", new BlockPos(100, 50, -7))
    );

  /// <summary>Fails when a step's read value is left off its ok line (V2).</summary>
  [Fact]
  public void A_step_prints_the_value_it_read_after_ok() {
    Assert.Equal(
      "[scenario]   2 expect {at} ok: game:stone",
      ScenarioOutput.StepOk("2", "expect", "{at}", "game:stone")
    );
    Assert.Equal(
      "[scenario]   2 expect {at} ok",
      ScenarioOutput.StepOk("2", "expect", "{at}", null)
    );
  }

  [Fact]
  public void A_failed_and_a_skipped_step_print_their_reason_and_state() {
    Assert.Equal(
      "[scenario]   3 wait 2 s FAIL: not met",
      ScenarioOutput.StepFail("3", "wait", "2 s", "not met")
    );
    Assert.Equal(
      "[scenario]   4 wait 1 s skipped",
      ScenarioOutput.Skipped("4", "wait", "1 s")
    );
  }

  /// <summary>Fails when a detail's line breaks are kept, which would split it into lines
  /// extools reads as steps (V3).</summary>
  [Fact]
  public void A_detail_stays_on_one_line() =>
    Assert.Equal("[scenario]   | a b", ScenarioOutput.Detail("a\nb"));

  /// <summary>Fails when the count line swaps its passed and failed figures (V4).</summary>
  [Fact]
  public void The_count_line_is_the_one_extools_matches() =>
    Assert.Equal(
      "[scenario] done: 3 case(s) run, 2 passed, 1 failed.",
      ScenarioOutput.Done(3, 2, 1)
    );

  /// <summary>Fails when seconds print with a different number of digits (V5).</summary>
  [Fact]
  public void Pass_and_fail_lines_give_one_decimal_of_seconds() {
    Assert.Equal(
      "[scenario] pass a/b (2.5 s)",
      ScenarioOutput.Pass("a/b", 2.46)
    );
    Assert.Equal(
      "[scenario] fail a/b step 3 suspect scenario (0.1 s)",
      ScenarioOutput.Fail("a/b", "3", "scenario", 0.06)
    );
    Assert.Equal(
      "[scenario] error a/b: no file",
      ScenarioOutput.Error("a/b", "no file")
    );
  }

  /// <summary>Fails when angle brackets reach chat unescaped (V6).</summary>
  [Fact]
  public void Chat_lines_escape_angle_brackets() =>
    Assert.Equal("a &lt;b&gt;", ScenarioOutput.ForChat("a <b>"));
}
