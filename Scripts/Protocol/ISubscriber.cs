namespace GGFolks.Protocol {

using React;

/// <summary>
/// Parent interface for Client and Session.
/// </summary>
public interface ISubscriber {

  /// <summary>
  /// The user id associated with the subscriber.
  /// </summary>
  Value<string> userId { get; }
}

}
