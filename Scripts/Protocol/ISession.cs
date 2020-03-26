namespace GGFolks.Protocol {

/// <summary>
/// Interface to be implemented by server sessions.
/// </summary>
public interface ISession : ISubscriber {

  /// <summary>
  /// Subscribes this session to the provided object.
  /// </summary>
  /// <param name="id">The client's requested id for the object.</param>
  void SubscribeToObject (uint id, DObject obj);

  /// <summary>
  /// Unsubscribes this session from the identified object.
  /// </summary>
  /// <param name="id">The client's id for the object.</param>
  void UnsubscribeFromObject (uint id);

  /// <summary>
  /// Sends an encoded message to the session on behalf of an object.
  /// </summary>
  void Send (DObject obj, byte[] data);
}

}
