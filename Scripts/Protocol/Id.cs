namespace GGFolks.Protocol {

using System;

/// <summary>
/// Specifies the protocol identifier of a type or a field.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
public class Id : Attribute {

  /// <summary>
  /// The value of the id attribute: the protocol identifier of the type or field.
  /// </summary>
  public readonly uint value;

  public Id (uint value) {
    this.value = value;
  }
}

}
