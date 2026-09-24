using System;
using System.IO;
using System.Reflection;
using ExpandedLib.Testing;
using NSubstitute;
using NSubstitute.Core;
using NSubstitute.Core.DependencyInjection;
using Vintagestory.API.Common;
using Xunit;
using AliasedLogger = Vintagestory.API.Common.ILogger;

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

  // Fails when a logger NSubstitute makes up for an API or world substitute logs where no check
  // reads.
  [Fact]
  public void A_warning_through_a_made_up_logger_fails() {
    Substitute.For<ICoreAPI>().Logger.Warning("valve {0} has no seat", 3);
    Substitute.For<IWorldAccessor>().Logger.Error("no seat");

    string? faults = FailOnWarningsCheck.Faults(
      nameof(A_warning_through_a_made_up_logger_fails)
    );

    Assert.Contains("unexpected Warning: valve 3 has no seat", faults);
    Assert.Contains("unexpected Error: no seat", faults);
  }

  // Fails when a substitute ILogger made through an alias, the non-generic For or as a second
  // interface logs where no check reads, or stops recording its calls for Received.
  [Fact]
  public void A_warning_through_a_substitute_logger_of_any_shape_fails() {
    var aliased = Substitute.For<AliasedLogger>();
    var untyped = (ILogger)Substitute.For([typeof(ILogger)], []);
    var paired = (ILogger)Substitute.For<IDisposable, ILogger>();

    aliased.Warning("aliased seat");
    untyped.Warning("untyped seat");
    paired.Warning("paired seat");

    aliased.Received(1).Warning("aliased seat");
    string? faults = FailOnWarningsCheck.Faults(
      nameof(A_warning_through_a_substitute_logger_of_any_shape_fails)
    );
    Assert.Contains("unexpected Warning: aliased seat", faults);
    Assert.Contains("unexpected Warning: untyped seat", faults);
    Assert.Contains("unexpected Warning: paired seat", faults);
  }

  // Fails when a substitute logger's formatting fault surfaces wrapped in the reflection call that
  // replays it, not as the fault a RecordingLogger raises.
  [Fact]
  public void A_bad_format_through_a_substitute_logger_throws_as_it_would_directly() {
    ILogger log = Substitute.For<ICoreAPI>().Logger;

    Assert.Throws<FormatException>(() => log.Warning("seat {1}", 0));
  }

  // Fails when the routed context takes thread state of its own, which strands a Returns or
  // Received on a substitute made before the swap.
  [Fact]
  public void The_routed_context_keeps_the_running_thread_state() {
    ISubstitutionContext running =
      NSubstituteDefaultFactory.CreateSubstitutionContext();

    Assert.Same(
      running.ThreadContext,
      SubstituteLogRouting.Routed(running).ThreadContext
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

  // Fails when a checked logger stays unregistered after a later Warning, as one a static holds
  // does, or when the next check reads the entries the first one already read.
  [Fact]
  public void A_warning_into_a_checked_logger_fails_the_next_check() {
    var world = new TestWorld();
    world.Log.Warning("first seat");
    Assert.NotNull(
      FailOnWarningsCheck.Faults(
        nameof(A_warning_into_a_checked_logger_fails_the_next_check)
      )
    );

    world.Log.Warning("second seat");

    string? faults = FailOnWarningsCheck.Faults(
      nameof(A_warning_into_a_checked_logger_fails_the_next_check)
    );
    Assert.Contains("unexpected Warning: second seat", faults);
    Assert.DoesNotContain("first seat", faults);
  }

  // Fails when an expectation outlives the check that read it.
  [Fact]
  public void An_expectation_covers_only_the_check_it_was_declared_for() {
    var world = new TestWorld();
    world.Log.Expect(EnumLogType.Warning, "no seat");
    world.Log.Warning("no seat");
    Assert.Null(
      FailOnWarningsCheck.Faults(
        nameof(An_expectation_covers_only_the_check_it_was_declared_for)
      )
    );

    world.Log.Warning("no seat");

    Assert.Contains(
      "unexpected Warning: no seat",
      FailOnWarningsCheck.Faults(
        nameof(An_expectation_covers_only_the_check_it_was_declared_for)
      )
    );
  }

  // Fails when Expect on a checked logger does not register it again.
  [Fact]
  public void An_expectation_on_a_checked_logger_is_checked() {
    var world = new TestWorld();
    Assert.Null(
      FailOnWarningsCheck.Faults(
        nameof(An_expectation_on_a_checked_logger_is_checked)
      )
    );

    world.Log.Expect(EnumLogType.Warning, "no seat");

    Assert.Contains(
      "was never logged",
      FailOnWarningsCheck.Faults(
        nameof(An_expectation_on_a_checked_logger_is_checked)
      )
    );
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
