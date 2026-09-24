using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.Sdk;

namespace ExpandedLib.Testing;

/// <summary>Fails every test in the assembly that leaves an unexpected Warning, Error or Fatal entry
/// in a <see cref="RecordingLogger"/>, or declares an entry through
/// <see cref="RecordingLogger.Expect"/> that never came.</summary>
/// <remarks>
/// <para>Opt in with <c>[assembly: FailOnWarnings]</c>. After each test it reads, then lets go,
/// every <see cref="RecordingLogger"/> created since the previous check. An entry logged into a
/// logger after its check, in <c>Dispose</c> or through a static, fails the next test checked; one
/// logged after the run's last check is never read.</para>
/// <para>Every <c>ILogger</c> NSubstitute creates, by <c>Substitute.For</c> in any form or for an
/// unset <c>Logger</c>, also logs into a <see cref="RecordingLogger"/> this check reads, which no
/// <see cref="RecordingLogger.Expect"/> can reach; it still records calls for <c>Received</c>.</para>
/// <para>Attribution relies on the assembly running its tests one at a time
/// (<c>parallelizeTestCollections: false</c>); under parallel collections a fault can be charged to
/// a test that ran beside the one that logged it.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailOnWarningsAttribute : BeforeAfterTestAttribute {
  /// <summary>Routes NSubstitute's loggers into <see cref="RecordingLogger"/>s for the rest of the
  /// process; see the remarks.</summary>
  public FailOnWarningsAttribute() => SubstituteLogRouting.Install();

  /// <summary>When true, a failing test's faults are written to standard error, one line each
  /// prefixed with <c>[FailOnWarnings]</c> and the test's full name, and the test passes.
  /// False by default.</summary>
  public bool ReportOnly { get; set; }

  /// <summary>Checks the loggers the test created.</summary>
  /// <param name="methodUnderTest">The test method that just ran.</param>
  /// <exception cref="FailOnWarningsException">The test left a fault and
  /// <see cref="ReportOnly"/> is false.</exception>
  public override void After(MethodInfo methodUnderTest) {
    List<string> faults = RecordingLogger.TakeFaults();
    if (faults.Count == 0)
      return;
    string test =
      $"{methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name}";
    if (ReportOnly) {
      foreach (string fault in faults)
        Console.Error.WriteLine($"[FailOnWarnings] {test}: {fault}");
      return;
    }
    throw new FailOnWarningsException(
      $"{test} logged what it did not expect:\n  {string.Join("\n  ", faults)}"
    );
  }
}

/// <summary>The failure <see cref="FailOnWarningsAttribute"/> raises; its message lists each
/// unexpected entry and each unmet <see cref="RecordingLogger.Expect"/>.</summary>
/// <param name="message">The failing test's full name, then one line per fault.</param>
public sealed class FailOnWarningsException(string message)
  : Exception(message);
