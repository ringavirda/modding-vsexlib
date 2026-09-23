using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit.Sdk;

namespace ExpandedLib.Testing;

/// <summary>Fails every test in the assembly that leaves an unexpected Warning, Error or Fatal entry
/// in a <see cref="RecordingLogger"/>, or declares an entry through
/// <see cref="RecordingLogger.Expect"/> that never came.</summary>
/// <remarks>
/// <para>Opt in with <c>[assembly: FailOnWarnings]</c>. After each test it reads every
/// <see cref="RecordingLogger"/> created since the previous test's check, in the test class's
/// constructor or in the body, and then lets them go; entries logged later, in <c>Dispose</c>, are
/// not seen.</para>
/// <para>Attribution relies on the assembly running its tests one at a time
/// (<c>parallelizeTestCollections: false</c>); under parallel collections a fault can be charged to
/// a test that ran beside the one that logged it.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailOnWarningsAttribute : BeforeAfterTestAttribute {
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
    string test = $"{methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name}";
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
public sealed class FailOnWarningsException(string message) : Exception(message);
