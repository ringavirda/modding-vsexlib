using System;
using System.IO;
using System.Reflection;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="FailOnWarningsAttribute"/>'s check run by hand after each body, over the
/// loggers the body created; the constructor drains every logger earlier tests left.</summary>
public class FailOnWarningsTests {
  public FailOnWarningsTests() => FailOnWarningsCheck.Drain();

  #region Faults

  // Fails when a RecordingLogger does not register itself on creation.
  [Fact]
  public void A_warning_from_a_world_built_in_the_body_fails() {
    var world = new TestWorld();
    world.Log.Warning("valve {0} has no seat", 3);

    Assert.Contains(
      "unexpected Warning: valve 3 has no seat",
      FailOnWarningsCheck.Faults(
        nameof(A_warning_from_a_world_built_in_the_body_fails)
      )
    );
  }

  // Fails when the check stops counting Error.
  [Fact]
  public void An_error_fails() {
    new TestWorld().Log.Error("no seat");

    Assert.Contains(
      "unexpected Error: no seat",
      FailOnWarningsCheck.Faults(nameof(An_error_fails))
    );
  }

  // Fails when the check stops counting Fatal.
  [Fact]
  public void A_fatal_entry_fails() {
    new TestWorld().Log.Fatal("no seat");

    Assert.Contains(
      "unexpected Fatal: no seat",
      FailOnWarningsCheck.Faults(nameof(A_fatal_entry_fails))
    );
  }

  // Fails when the check skips entries Clear removed from Entries.
  [Fact]
  public void A_cleared_warning_still_fails() {
    var world = new TestWorld();
    world.Log.Warning("no seat");
    world.Log.Clear();

    Assert.Empty(world.Log.Entries);
    Assert.Contains(
      "unexpected Warning: no seat",
      FailOnWarningsCheck.Faults(nameof(A_cleared_warning_still_fails))
    );
  }

  // Fails when the check counts every log type, not only Warning, Error and Fatal.
  [Fact]
  public void Notifications_and_debug_entries_pass() {
    var world = new TestWorld();
    world.Log.Notification("seated");
    world.Log.Debug("seated");

    Assert.Null(
      FailOnWarningsCheck.Faults(nameof(Notifications_and_debug_entries_pass))
    );
  }

  // Fails when a checked logger stays registered, so the next test is charged with its entries.
  [Fact]
  public void A_logger_is_checked_once() {
    new TestWorld().Log.Warning("no seat");

    Assert.NotNull(
      FailOnWarningsCheck.Faults(nameof(A_logger_is_checked_once))
    );
    Assert.Null(FailOnWarningsCheck.Faults(nameof(A_logger_is_checked_once)));
  }

  #endregion

  #region Expect

  // Fails when an expectation stops absorbing the entries it matches, declared before or after
  // them.
  [Fact]
  public void Expected_warnings_pass_however_many_and_whenever_declared() {
    var world = new TestWorld();
    world.Log.Warning("valve 1 has no seat");
    world.Log.Expect(EnumLogType.Warning, "has no seat");
    world.Log.Warning("valve 2 has no seat");

    Assert.Null(
      FailOnWarningsCheck.Faults(
        nameof(Expected_warnings_pass_however_many_and_whenever_declared)
      )
    );
  }

  // Fails when an expectation no entry matched is let through.
  [Fact]
  public void An_expectation_never_logged_fails() {
    new TestWorld().Log.Expect(EnumLogType.Warning, "has no seat");

    Assert.Contains(
      "expected Warning containing \"has no seat\" was never logged",
      FailOnWarningsCheck.Faults(nameof(An_expectation_never_logged_fails))
    );
  }

  // Fails when Expect matches an entry of another type.
  [Fact]
  public void An_expectation_of_another_type_does_not_match() {
    var world = new TestWorld();
    world.Log.Error("has no seat");
    world.Log.Expect(EnumLogType.Warning, "has no seat");

    string? faults = FailOnWarningsCheck.Faults(
      nameof(An_expectation_of_another_type_does_not_match)
    );
    Assert.Contains("unexpected Error: has no seat", faults);
    Assert.Contains("was never logged", faults);
  }

  // Fails when Expect ignores its fragment.
  [Fact]
  public void An_expectation_with_another_fragment_does_not_match() {
    var world = new TestWorld();
    world.Log.Warning("has no seat");
    world.Log.Expect(EnumLogType.Warning, "has no gasket");

    Assert.Contains(
      "unexpected Warning: has no seat",
      FailOnWarningsCheck.Faults(
        nameof(An_expectation_with_another_fragment_does_not_match)
      )
    );
  }

  // Fails when Expect accepts a null fragment.
  [Fact]
  public void Expect_refuses_a_null_fragment() {
    Assert.Throws<ArgumentNullException>(() =>
      new RecordingLogger().Expect(EnumLogType.Warning, null!)
    );
  }

  #endregion

  #region ReportOnly

  // Fails when ReportOnly still throws, or stops writing the test and its fault to standard error.
  [Fact]
  public void Report_only_writes_the_fault_and_passes() {
    new TestWorld().Log.Warning("no seat");
    var err = new StringWriter();
    TextWriter was = Console.Error;
    Console.SetError(err);
    try {
      new FailOnWarningsAttribute { ReportOnly = true }.After(
        typeof(FailOnWarningsTests).GetMethod(
          nameof(Report_only_writes_the_fault_and_passes)
        )!
      );
    } finally {
      Console.SetError(was);
    }

    Assert.Equal(
      "[FailOnWarnings] ExpandedLib.Tests.FailOnWarningsTests."
        + "Report_only_writes_the_fault_and_passes: unexpected Warning: no seat",
      err.ToString().Trim()
    );
  }

  #endregion
}

/// <summary>A logger created in the test class's constructor, before the body runs, is checked
/// with the body's.</summary>
public class FailOnWarningsConstructorTests {
  private readonly TestWorld _world;

  public FailOnWarningsConstructorTests() {
    FailOnWarningsCheck.Drain();
    _world = new TestWorld();
    _world.Log.Warning("from the constructor");
  }

  // Fails when the check reads only loggers created after the constructor ran.
  [Fact]
  public void A_warning_from_a_world_built_in_the_constructor_fails() {
    new TestWorld().Log.Warning("from the body");

    string? faults = FailOnWarningsCheck.Faults(
      nameof(A_warning_from_a_world_built_in_the_constructor_fails),
      typeof(FailOnWarningsConstructorTests)
    );
    Assert.Contains("unexpected Warning: from the constructor", faults);
    Assert.Contains("unexpected Warning: from the body", faults);
    Assert.StartsWith(
      "ExpandedLib.Tests.FailOnWarningsConstructorTests."
        + "A_warning_from_a_world_built_in_the_constructor_fails logged what it did not expect:",
      faults
    );
  }
}

internal static class FailOnWarningsCheck {
  // Runs the check for the named test; null when it passes, else the failure's message.
  internal static string? Faults(string test, Type? owner = null) {
    try {
      new FailOnWarningsAttribute().After(
        (owner ?? typeof(FailOnWarningsTests)).GetMethod(test)!
      );
      return null;
    } catch (FailOnWarningsException e) {
      return e.Message;
    }
  }

  // Takes every logger earlier tests left registered, without failing or printing.
  internal static void Drain() {
    TextWriter was = Console.Error;
    Console.SetError(TextWriter.Null);
    try {
      new FailOnWarningsAttribute { ReportOnly = true }.After(
        typeof(FailOnWarningsCheck).GetMethod(
          nameof(Drain),
          BindingFlags.NonPublic | BindingFlags.Static
        )!
      );
    } finally {
      Console.SetError(was);
    }
  }
}
