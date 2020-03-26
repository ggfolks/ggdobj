namespace GGFolks.Protocol {

using System;

/// <summary>
/// An exception whose message can be sent to the client.
/// </summary>
public class FriendlyException : Exception {

  /// <summary>
  /// Creates a new friendly exception with the specified message.
  /// </summary>
  public FriendlyException (string message) : base(message) {}
}

}
