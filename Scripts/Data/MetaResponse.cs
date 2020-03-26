namespace GGFolks.Data {

using Protocol;

/// <summary>
/// Superclass of responses sent to the meta queue.
/// </summary>
[Subtypes(typeof(SubscribeFailed))]
public abstract class MetaResponse {

  /// <summary>
  /// Notifies the client that a subscription request has failed.
  /// </summary>
  [Id(1)]
  public class SubscribeFailed : MetaResponse {

    /// <summary>
    /// The local id of the object.
    /// </summary>
    [Id(1)] public uint id;

    /// <summary>
    /// The reason why subscription failed.
    /// </summary>
    [Id(2)] public string cause;
  }
}

}
