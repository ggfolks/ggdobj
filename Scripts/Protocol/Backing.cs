namespace GGFolks.Protocol {

using System;

/// <summary>
/// Specifies the backing type of a collection field.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class Backing : Attribute {

  /// <summary>
  /// The type of backing for the collection field.
  /// </summary>
  public readonly BackingType type;

  public Backing (BackingType type) {
    this.type = type;
  }
}

}
