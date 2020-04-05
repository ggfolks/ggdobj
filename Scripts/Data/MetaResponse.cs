namespace GGFolks.Data {

using Protocol;

/// <summary>
/// Superclass of responses sent to the meta queue.
/// </summary>
[Subtypes(typeof(AuthenticateFailed), typeof(SubscribeFailed))]
public abstract class MetaResponse {

  /// <summary>
  /// Represents a request to authenticate.
  /// </summary>
  [Id(1)]
  public class AuthenticateFailed : MetaResponse {

    /// <summary>
    /// The user id.
    /// </summary>
    [Id(1)] public string userId;

    /// <summary>
    /// The reason why authentication failed.
    /// </summary>
    [Id(2)] public string cause;
  }

  /// <summary>
  /// Notifies the client that a subscription request has failed.
  /// </summary>
  [Id(2)]
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
