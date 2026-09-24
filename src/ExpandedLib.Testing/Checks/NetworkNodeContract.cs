using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Checks;
using ExpandedLib.Networks;

namespace ExpandedLib.Testing;

/// <summary>The structural rules a definition must obey to join a network graph: a node def must
/// declare a <c>type</c> variant state and the orientation scheme it ships, and a declared
/// <c>BEBehaviorNetworkMember</c> must name the network it joins. A node is a def whose
/// <c>class</c> names a <see cref="BlockNetworkNode"/> or that declares <c>ExOrientable</c> in
/// <c>network</c> mode.</summary>
public static class NetworkNodeContract {
  /// <summary>Every violation of the type-group and membership rules in <paramref name="asm"/>, as
  /// a human-readable line.</summary>
  public static IReadOnlyList<string> Violations(string domain, Assembly asm) =>
    [.. TypeGroupViolations(domain, asm), .. MembershipViolations(domain, asm)];

  /// <summary>Every network-node def in <paramref name="asm"/> whose <c>ExOrientable</c> declaration
  /// does not agree with the states it actually ships, reporting how many defs were examined.</summary>
  public static IReadOnlyList<string> SchemeViolations(
    string domain,
    Assembly asm,
    out int defsChecked
  ) =>
    NetworkNodeContractCheck.SchemeViolations(
      new AssemblyCheckSource((domain, asm)),
      domain,
      out defsChecked
    );

  /// <summary>Every network-node def in <paramref name="asm"/> that declares no <c>type</c> variant
  /// state.</summary>
  public static IReadOnlyList<string> TypeGroupViolations(
    string domain,
    Assembly asm
  ) =>
    [
      .. NetworkNodeContractCheck.TypeGroupViolations(
        new AssemblyCheckSource((domain, asm)),
        domain
      ),
    ];

  /// <summary>Every declared network membership in <paramref name="asm"/>,
  /// <c>BEBehaviorNetworkMember</c> or a subclass, that names no <c>networkType</c>.</summary>
  public static IReadOnlyList<string> MembershipViolations(
    string domain,
    Assembly asm
  ) =>
    [
      .. NetworkNodeContractCheck.MembershipViolations(
        new AssemblyCheckSource((domain, asm)),
        domain
      ),
    ];
}
