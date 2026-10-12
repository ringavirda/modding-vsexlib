#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Scenarios;

namespace ExpandedLib.Testing;

/// <summary>
/// A setup file read and validated: its keys, ids, layout, steps, readings and checks (setups.md,
/// The file). <see cref="SetupScene.Build"/> builds it into a world.
/// </summary>
public sealed class SetupFile
{
  internal SetupFileModel Model { get; }

  private SetupFile(SetupFileModel model)
  {
    Model = model;
  }

  /// <summary>The name the file was read under, as messages name it.</summary>
  public string Name => Model.File;

  /// <summary>The file's <c>setup</c> id.</summary>
  public string Setup => Model.Setup;

  /// <summary>The ids of the mods the setup needs besides the game and exlib, in load order.</summary>
  public IReadOnlyList<string> Mods => Model.Mods;

  /// <summary>The first game series the file runs on, such as <c>1.22</c>.</summary>
  public string Game => Model.Game;

  /// <summary>The names of the file's <c>variants</c>, in file order.</summary>
  internal IReadOnlyList<string> VariantNames =>
    [.. Model.Variants.Select(v => v.Name)];

  /// <summary>The ids <c>tickOrders</c> names, in file order; empty when the file names none.</summary>
  internal IReadOnlyList<string> TickOrderIds => Model.TickOrders;

  /// <summary>The names of the variants that run in every tick order, in file order.</summary>
  internal IReadOnlyList<string> OrderedVariantNames =>
    [
      .. Model
        .Variants.Where(v => v.Body["tickOrders"] != null)
        .Select(v => v.Name),
    ];

  /// <summary>The file for the harness case <c>&lt;setup&gt;/&lt;name&gt;</c>: the main case with
  /// the variant's delta applied.</summary>
  /// <exception cref="ArgumentException">The file has no variant by that name.</exception>
  /// <exception cref="InvalidOperationException">The variant's layers or entry replacements break
  /// a rule of setups.md, What the reader refuses.</exception>
  internal SetupFile ForVariant(string name)
  {
    SetupVariant variant =
      Model.Variants.FirstOrDefault(v => v.Name == name)
      ?? throw new ArgumentException(
        $"{Name} has no variant \"{name}\".",
        nameof(name)
      );
    try
    {
      return new SetupFile(Model.ForVariant(variant));
    }
    catch (SetupFileException e)
    {
      throw new InvalidOperationException(e.Message, e);
    }
  }

  /// <summary>Reads and validates <paramref name="text"/>.</summary>
  /// <param name="text">A setup file's JSON.</param>
  /// <param name="name">What messages call the file.</param>
  /// <returns>The validated file.</returns>
  /// <exception cref="InvalidOperationException">The file is malformed, names an unknown key, breaks
  /// a rule of setups.md (What the reader refuses) or names an id nobody declares; the message names
  /// the file, where and the key.</exception>
  public static SetupFile Parse(string text, string name)
  {
    try
    {
      return new SetupFile(SetupFileModel.Parse(text, name));
    }
    catch (SetupFileException e)
    {
      throw new InvalidOperationException(e.Message, e);
    }
  }

  /// <summary>Reads and validates the file at <paramref name="path"/>.</summary>
  /// <param name="path">A setup file on disk; it names the file in messages.</param>
  /// <returns>The validated file.</returns>
  /// <exception cref="InvalidOperationException">As <see cref="Parse"/> throws it.</exception>
  /// <exception cref="IOException">The file cannot be read.</exception>
  public static SetupFile Load(string path) =>
    Parse(File.ReadAllText(path), path);
}
#endif
