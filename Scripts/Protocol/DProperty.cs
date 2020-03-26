namespace GGFolks.Protocol {

using System;
using System.Threading.Tasks;
using Firebase.Firestore;

/// <summary>
/// Base class for properties of distributed objects.
/// </summary>
public abstract class DProperty {

  /// <summary>
  /// The name of the property in Firestore, if applicable.
  /// </summary>
  public string firestoreField { get; private set; }

  /// <summary>
  /// Initializes the property after creation.
  /// </summary>
  public virtual void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    _owner = owner;
    _id = id;
    _ctx = ctx;

    firestoreField = $"{name}${id}";
  }

  /// <summary>
  /// Writes the current value of the property to the supplied encoder.
  /// </summary>
  public virtual void Encode (Encoder encoder) {
    // nothing by default
  }

  /// <summary>
  /// Reads the value of the property from the supplied decoder.
  /// </summary>
  public virtual void Decode (Decoder decoder, WireType wireType) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Reads a set add message.
  /// </summary>
  public virtual void DecodeSetAdd (Decoder decoder, WireType wireType) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Reads a set remove message.
  /// </summary>
  public virtual void DecodeSetRemove (Decoder decoder, WireType wireType) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Reads a map set message.
  /// </summary>
  public virtual void DecodeMapSet (Decoder decoder, WireType keyType, WireType valueType) {
    decoder.Skip(keyType);
    decoder.Skip(valueType);
  }

  /// <summary>
  /// Reads a map remove message.
  /// </summary>
  public virtual void DecodeMapRemove (Decoder decoder, WireType wireType) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Reads a queue post message.
  /// </summary>
  public virtual void DecodeQueuePost (Decoder decoder, WireType wireType, ISession session) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Reads a queue receive message.
  /// </summary>
  public virtual void DecodeQueueReceive (Decoder decoder, WireType wireType) {
    decoder.Skip(wireType);
  }

  /// <summary>
  /// Called on the server to resolve an object on behalf of a session.
  /// </summary>
  /// <param name="session">The session attempting to subscribe.</param>
  /// <param name="path">The path of the object to which the client is subscribing.</param>
  /// <param name="index">The index of this property in the path.</param>
  /// <returns>If successful, the resolved dobject.</returns>
  public virtual Task<DObject> Resolve (ISession session, Path path, int index) {
    throw new Exception("Not a collection property.");
  }

  /// <summary>
  /// Extracts the property value from the provided Firestore snapshot.
  /// </summary>
  public virtual void Extract (DocumentSnapshot snapshot) {
    // nothing by default
  }

  protected void RequireClient () {
    if (_owner.client == null) throw new Exception("Operation not available on server.");
  }

  protected void RequireServer () {
    if (_owner.client != null) throw new Exception("Operation not available on client.");
  }

  protected void RequireServerOrFirestore () {
    if (!(_owner.client == null || _owner.backing == BackingType.Firestore)) {
      throw new Exception("Operation only available on server or Firestore.");
    }
  }

  protected DObject _owner;
  protected uint _id;
  protected object _ctx;
}

}
