namespace GGFolks.Data {

using Protocol;

/// <summary>
/// Superclass of requests sent to the meta queue.
/// </summary>
[Subtypes(typeof(Authenticate), typeof(Subscribe), typeof(Unsubscribe))]
public abstract class MetaRequest {

  /// <summary>
  /// Represents a request to authenticate.
  /// </summary>
  [Id(1)]
  public class Authenticate : MetaRequest {

    /// <summary>
    /// The user id.
    /// </summary>
    [Id(1)] public string userId;

    /// <summary>
    /// The authentication token.
    /// </summary>
    [Id(2)] public string token;
  }

  /// <summary>
  /// Represents a request to subscribe to an object.
  /// </summary>
  [Id(2)]
  public class Subscribe : MetaRequest {

    /// <summary>
    /// The local id of the object.
    /// </summary>
    [Id(1)] public uint id;

    /// <summary>
    /// The path of the object.
    /// </summary>
    [Id(2)] public Path path;
  }

  /// <summary>
  /// Represents a request to unsubscribe from an object.
  /// </summary>
  [Id(3)]
  public class Unsubscribe : MetaRequest {

    /// <summary>
    /// The local id of the object.
    /// </summary>
    [Id(1)] public uint id;
  }
}

}
