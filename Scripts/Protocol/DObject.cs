namespace GGFolks.Protocol {

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Firebase.Firestore;

using Util;

/// <summary>
/// Base class for distributed objects.
/// </summary>
public abstract class DObject : Disposable {

  /// <summary>
  /// The possible states that the DObject may be in.
  /// </summary>
  public enum State { Resolving, Failed, Active, Disconnected, Disposed }

  /// <summary>
  /// The current state of the DObject (whether it is resolving, failed, etc.)
  /// </summary>
  public State state {
    get => _state;
    private set {
      if (_state == value) return;
      _state = value;
      stateChanged?.Invoke(this, _state);
    }
  }

  /// <summary>
  /// A reference to the owning client, or null if this is the server.
  /// </summary>
  public IClient client { get; private set; }

  /// <summary>
  /// The path that uniquely identifies the dobj.
  /// </summary>
  public Path path { get; private set; }

  /// <summary>
  /// The id that identified the dobj on the client.
  /// </summary>
  public uint id { get; private set; }

  /// <summary>
  /// The type of backing used for the dobj.
  /// </summary>
  public BackingType backing { get; private set; } = BackingType.Server;

  /// <summary>
  /// An event fired when the state of the DObject changes (e.g., when it resolves).
  /// </summary>
  public event EventHandler<State> stateChanged;

  /// <summary>
  /// An event fired when the object generates a message.  On the server, this means that the
  /// message should be broadcast to all subscribed sessions (prefixed with the session's identifier
  /// for the object).  On the client, it means that the message should be sent to the server.
  /// </summary>
  public event EventHandler<byte[]> messageGenerated;

  /// <summary>
  /// An event fired when a subscriber subscribes to the object.
  /// </summary>
  public event EventHandler<ISubscriber> subscribed;

  /// <summary>
  /// An event fired when a subscriber unsubscribes from the object.
  /// </summary>
  public event EventHandler<ISubscriber> unsubscribed;

  /// <summary>
  /// Type for delegates that asynchronously check whether a subscriber can access the object.
  /// </summary>
  public delegate Task<bool> CanAccess (ISubscriber subscriber);

  /// <summary>
  /// Type for delegates that asynchronously populate the object.
  /// </summary>
  public delegate Task Populate (DObject obj);

  public DObject () {
    var type = GetType();
    foreach (var propertyInfo in type.GetProperties()) {
      var idAttributes = (Id[])propertyInfo.GetCustomAttributes(typeof(Id), false);
      if (idAttributes.Length == 0) continue;
      var property = (DProperty)propertyInfo.GetValue(this);
      if (property == null) {
        var constructor = propertyInfo.PropertyType.GetConstructor(Type.EmptyTypes);
        property = (DProperty)constructor.Invoke(null);
        propertyInfo.SetValue(this, property);
      }
      var backing = this.backing;
      var backingAttributes = (Backing[])propertyInfo.GetCustomAttributes(typeof(Backing), false);
      if (backingAttributes.Length > 0 && backingAttributes[0].type != BackingType.Default) {
        backing = backingAttributes[0].type;
      }
      property.Init(
        this, propertyInfo.Name, idAttributes[0].value,
        $"{type.FullName}.{propertyInfo.Name}", backing);
      _properties.Add(idAttributes[0].value, property);
    }
    disposer.Add(() => state = State.Disposed);
  }

  /// <summary>
  /// Initializes the object for Firestore operation.
  /// </summary>
  public async void FirestoreInit (
      IClient client, Path path, CanAccess canAccess, Populate populate) {
    this.client = client;
    this.path = path;
    backing = BackingType.Firestore;
    _canAccess = canAccess;
    _populate = populate;

    var firestore = await client.firestore;
    _document = firestore.Document(client.GetFirestorePath(path));

    // wait for the userId to resolve/re-resolve
    disposer.Add(client.userId.OnValue(userId => {
      if (userId != null) FirestoreResolve();
    }));
  }

  /// <summary>
  /// Initializes the object for operation on the client.
  /// </summary>
  public void ClientInit (IClient client, Path path, uint id) {
    this.client = client;
    this.path = path;
    this.id = id;
  }

  /// <summary>
  /// Initializes the object for operation on the server.
  /// </summary>
  public void ServerInit (Path path) {
    state = State.Active;
    this.path = path;
  }

  /// <summary>
  /// Called on the server to resolve an object on behalf of a session.
  /// </summary>
  /// <param name="session">The session attempting to subscribe.</param>
  /// <param name="path">The path of the object to which the client is subscribing.</param>
  /// <param name="index">The index of this object in the path.</param>
  /// <returns>If successful, the resolved dobject.</returns>
  public async Task<DObject> Resolve (ISession session, Path path, int index) {
    var (id, _) = path.elements[index];
    DProperty property;
    if (_properties.TryGetValue(id, out property)) {
      return await property.Resolve(session, path, index);
    } else throw new Exception($"Unknown property {id}.");
  }

  /// <summary>
  /// Notifies the object that a subscriber has subscribed.
  /// </summary>
  public void OnSubscribe (ISubscriber subscriber) {
    subscribed?.Invoke(this, subscriber);
  }

  /// <summary>
  /// Notifies the object that a subscriber has unsubscribed.
  /// </summary>
  public void OnUnsubscribe (ISubscriber subscriber) {
    unsubscribed?.Invoke(this, subscriber);
  }

  /// <summary>
  /// Encodes the full state of the object to the supplied encoder.
  /// </summary>
  public void ServerEncode (Encoder encoder) {
    encoder.WriteVarUInt((uint)MessageType.Sync);
    foreach (var property in _properties.Values) property.Encode(encoder);
  }

  /// <summary>
  /// Decodes a message for this object received on the client.
  /// </summary>
  public void ClientDecode (Decoder decoder) {
    var messageType = (MessageType)decoder.ReadVarUInt();
    if (state != State.Active && messageType != MessageType.Sync) {
      Debug.LogWarning($"Got property update before sync [messageType={messageType}].");
    }
    switch (messageType) {
      case MessageType.Sync: {
        var end = decoder.BaseStream.Length;
        while (decoder.BaseStream.Position < end) DecodeProperty(decoder);
        state = State.Active;
        break;
      }
      case MessageType.ValueChange: {
        DecodeProperty(decoder);
        break;
      }
      case MessageType.SetAdd: {
        var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
        DProperty property;
        if (_properties.TryGetValue(id, out property)) property.DecodeSetAdd(decoder, wireType);
        else decoder.Skip(wireType); // unknown property; skip over
        break;
      }
      case MessageType.SetRemove: {
        var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
        DProperty property;
        if (_properties.TryGetValue(id, out property)) property.DecodeSetRemove(decoder, wireType);
        else decoder.Skip(wireType); // unknown property; skip over
        break;
      }
      case MessageType.MapSet: {
        var (id, keyType, valueType) = Decoder.DecodeIdWireTypes(decoder.ReadVarUInt());
        DProperty property;
        if (_properties.TryGetValue(id, out property)) {
          property.DecodeMapSet(decoder, keyType, valueType);
        } else {
          decoder.Skip(keyType);
          decoder.Skip(valueType);
        }
        break;
      }
      case MessageType.MapRemove: {
        var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
        DProperty property;
        if (_properties.TryGetValue(id, out property)) property.DecodeMapRemove(decoder, wireType);
        else decoder.Skip(wireType); // unknown property; skip over
        break;
      }
      case MessageType.QueueReceive: {
        var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
        DProperty property;
        if (_properties.TryGetValue(id, out property)) {
          property.DecodeQueueReceive(decoder, wireType);
        } else decoder.Skip(wireType); // unknown property; skip over
        break;
      }
      default:
        Debug.LogWarning($"Received unknown message type {messageType}.");
        break;
    }
  }

  /// <summary>
  /// Decodes a message for this object received on the server.
  /// </summary>
  public void ServerDecode (Decoder decoder, ISession session) {
    var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
    DProperty property;
    if (_properties.TryGetValue(id, out property)) {
      property.DecodeQueuePost(decoder, wireType, session);
    } else decoder.Skip(wireType); // unknown property; skip over
  }

  /// <summary>
  /// Notifies the object that one of its value properties has changed.
  /// </summary>
  public void OnValueChange<T> (DValue<T> value) {
    if (messageGenerated != null) {
      _encoder.WriteVarUInt((uint)MessageType.ValueChange);
      value.Encode(_encoder);
      messageGenerated(this, _encoder.Finish());
    }
    if (_document != null) {
      SetDocumentAsync(new Dictionary<string, object>() {
        { value.firestoreField, value.ConvertToFirestore(value.current) },
      });
    }
  }

  /// <summary>
  /// Notifies the object that an element has been added to one of its set properties.
  /// </summary>
  public void OnSetAdd<T> (DSet<T> set, T value) {
    if (messageGenerated != null) {
      _encoder.WriteVarUInt((uint)MessageType.SetAdd);
      set.EncodeValue(_encoder, value);
      messageGenerated(this, _encoder.Finish());
    }
    if (_document != null) {
      SetDocumentAsync(new Dictionary<string, object>() {
        { set.firestoreField, new Dictionary<string, object>() {{ value.ToString(), true }} },
      });
    }
  }

  /// <summary>
  /// Notifies the object that an element has been removed from one of its set properties.
  /// </summary>
  public void OnSetRemove<T> (DSet<T> set, T value) {
    if (messageGenerated != null) {
      _encoder.WriteVarUInt((uint)MessageType.SetRemove);
      set.EncodeValue(_encoder, value);
      messageGenerated(this, _encoder.Finish());
    }
    if (_document != null) {
      SetDocumentAsync(new Dictionary<string, object>() {
        {
          set.firestoreField,
          new Dictionary<string, object>() {{ value.ToString(), FieldValue.Delete }}
        },
      });
    }
  }

  /// <summary>
  /// Notifies the object that an entry has been set in one of its map properties.
  /// </summary>
  public void OnMapSet<TKey, TValue> (DMap<TKey, TValue> map, TKey key, TValue value) {
    if (messageGenerated != null) {
      _encoder.WriteVarUInt((uint)MessageType.MapSet);
      map.EncodeKeyValue(_encoder, key, value);
      messageGenerated(this, _encoder.Finish());
    }
    if (_document != null) {
      SetDocumentAsync(new Dictionary<string, object>() {
        {
          map.firestoreField,
          new Dictionary<string, object>() {{ key.ToString(), map.ConvertValueToFirestore(value) }}
        },
      });
    }
  }

  /// <summary>
  /// Notifies the object that an entry has been removed in one of its map properties.
  /// </summary>
  public void OnMapRemove<TKey, TValue> (DMap<TKey, TValue> map, TKey key) {
    if (messageGenerated != null) {
      _encoder.WriteVarUInt((uint)MessageType.MapRemove);
      map.EncodeKey(_encoder, key);
      messageGenerated(this, _encoder.Finish());
    }
    if (_document != null) {
      SetDocumentAsync(new Dictionary<string, object>() {
        {
          map.firestoreField,
          new Dictionary<string, object>() {{ key.ToString(), FieldValue.Delete }}
        },
      });
    }
  }

  /// <summary>
  /// Notifies the object that an upstream message has been posted to one of its queue properties.
  /// </summary>
  public void OnPost<TUp, TDown> (DQueue<TUp, TDown> queue, TUp message) {
    if (messageGenerated == null) return;
    _encoder.WriteVarUInt(id);
    queue.EncodeUp(_encoder, message);
    messageGenerated(this, _encoder.Finish());
  }

  /// <summary>
  /// Notifies the object that a downstream message has been broadcast on one of its queue
  /// properties.
  /// </summary>
  public void OnBroadcast<TUp, TDown> (DQueue<TUp, TDown> queue, TDown message) {
    if (messageGenerated == null) return;
    _encoder.WriteVarUInt((uint)MessageType.QueueReceive);
    queue.EncodeDown(_encoder, message);
    messageGenerated(this, _encoder.Finish());
  }

  /// <summary>
  /// Notifies the object that a downstream message has been sent on one of its queue properties.
  /// </summary>
  public void OnSend<TUp, TDown> (DQueue<TUp, TDown> queue, TDown message, ISession session) {
    _encoder.WriteVarUInt((uint)MessageType.QueueReceive);
    queue.EncodeDown(_encoder, message);
    session.Send(this, _encoder.Finish());
  }

  /// <summary>
  /// Notifies the object that the owning client's connection has closed.
  /// </summary>
  public void OnDisconnect () {
    state = State.Disconnected;
  }

  private void DecodeProperty (Decoder decoder) {
    var (id, wireType) = Decoder.DecodeIdWireType(decoder.ReadVarUInt());
    DProperty property;
    if (_properties.TryGetValue(id, out property)) property.Decode(decoder, wireType);
    else decoder.Skip(wireType); // unknown property; skip over
  }

  private async void FirestoreResolve () {
    if (_canAccess != null) {
      var accessible = await _canAccess(client);
      if (!accessible) {
        Debug.LogWarning($"Denied access to object [which={this}].");
        state = State.Failed;
        return;
      }
    }
    if (state != State.Resolving) return; // only want to do the following once

    if (_populate != null) await _populate(this);

    var registration = _document.Listen(OnDocumentChanged);
    disposer.Add(() => registration.Stop());

    var snapshot = await _document.GetSnapshotAsync();
    OnDocumentChanged(snapshot);
    state = State.Active;

    subscribed?.Invoke(this, client);
    disposer.Add(() => unsubscribed?.Invoke(this, client));
  }

  private void OnDocumentChanged (DocumentSnapshot snapshot) {
    foreach (var property in _properties.Values) property.Extract(snapshot);
  }

  private async void SetDocumentAsync (Dictionary<string, object> dictionary) {
    try {
      await _document.SetAsync(dictionary, SetOptions.MergeAll);
    } catch (Exception e) {
      Debug.LogWarning($"Failed to set Firestore [dictionary={dictionary}, exception={e}].");
    }
  }

  private enum MessageType { Sync, ValueChange, SetAdd, SetRemove, MapSet, MapRemove, QueueReceive }

  private State _state = State.Resolving;
  private Dictionary<uint, DProperty> _properties = new Dictionary<uint, DProperty>();
  private Encoder _encoder = new Encoder();
  private DocumentReference _document;
  private CanAccess _canAccess;
  private Populate _populate;
}

}
