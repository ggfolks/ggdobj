namespace GGFolks.Protocol {

using System;

/// <summary>
/// Specifies the subtypes of a transmittable type.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class Subtypes : Attribute {

  /// <summary>
  /// The value of the subtypes attribute: the subtypes available for transmission or reception.
  /// </summary>
  public readonly Type[] value;

  public Subtypes (params Type[] value) {
    this.value = value;
  }
}

}
