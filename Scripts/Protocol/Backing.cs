namespace GGFolks.Protocol {

using System;

/// <summary>
/// Specifies the backing type of a collection property.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class Backing : Attribute {

  /// <summary>
  /// The type of backing for the collection property.
  /// </summary>
  public readonly BackingType type;

  public Backing (BackingType type) {
    this.type = type;
  }
}

}
