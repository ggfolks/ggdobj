namespace GGFolks.Client {

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;
using Firebase.Firestore;

using Data;
using Protocol;
using React;
using Util;

/// <summary>
/// The web socket client used both for editor testing and standalone operation.
/// </summary>
public class Client<TRoot> : Disposable, IClient where TRoot : AbstractRootObject, new() {

  /// <summary>
  /// The top-level dobject.
  /// </summary>
  public readonly TRoot rootObject = new TRoot();

  /// <summary>
  /// Checks whether we are currently connected to the server.
  /// </summary>
  public bool connected { get => _webSocket != null && _webSocket.State == WebSocketState.Open; }

  // defined by ISubscriber
  public Value<string> userId { get => _userId; }

  // defined by IClient
  public Task<FirebaseFirestore> firestore { get => AbstractApp<TRoot>.instance.firestore; }

  /// <summary>
  /// Initializes the game client.
  /// </summary>
  /// <param name="url">The WebSocket URL to connect to.</param>
  public Client (string url) {
    _url = url;
    rootObject = Resolve<TRoot>(Path.root, BackingType.Server);
    rootObject.metaq.received += OnMetaQueueReceive;
    Init();
  }

  // defined by IClient
  public T Resolve<T> (
      Path path, BackingType backing, DCollection<T>.CanAccess canAccess = null,
      DCollection<T>.Populate populate = null) where T : DObject, new() {
    WeakReference reference;
    if (_objectsByPath.TryGetValue(path, out reference)) {
      var obj = (T)reference.Target;
      if (obj != null) return obj;
    }
    var newObj = new T();
    _objectsByPath.Add(path, reference = new WeakReference(newObj));
    newObj.disposer.Add(() => Unresolve(newObj));
    if (backing == BackingType.Firestore) {
      newObj.FirestoreInit(
        this,
        path,
        canAccess == null
          ? (DObject.CanAccess)null
          : subscriber => canAccess(subscriber, path.elements[path.elements.Length - 1].key),
        populate == null ? (DObject.Populate)null : obj => populate(newObj)
      );
      return newObj;
    }
    var id = _recyclableIds.count > 0 ? _recyclableIds.TakeLowest() : _nextId++;
    _objectsById.Add(id, reference);
    newObj.ClientInit(this, path, id);
    newObj.messageGenerated += OnMessageGenerated;
    if (connected) rootObject.metaq.Post(new MetaRequest.Subscribe() { id = id, path = path });
    return newObj;
  }

  // defined by IClient
  public string GetFirestorePath (Path path) {
    var builder = new System.Text.StringBuilder();
    var type = rootObject.GetType();
    foreach (var pair in path.elements) {
      var field = GetCollectionFields(type)[pair.id];
      if (builder.Length > 0) builder.Append('/');
      builder.Append($"{field.name}${pair.id}/{pair.key}");
      type = field.type;
    }
    return builder.ToString();
  }

  protected override async void Dispose (bool disposing) {
    if (_webSocket == null) return;
    _reconnect = false;
    await _webSocket.Close();
  }

  private async void Init () {
    var firebaseAuth = await AbstractApp<TRoot>.instance.firebaseAuth;
    Action updateUserIdToken = async () => {
      var currentUser = firebaseAuth.CurrentUser;
      if (currentUser == null) return;
      _token = await currentUser.TokenAsync(false);
      _userId.Update(currentUser.UserId);
      if (connected) {
        rootObject.metaq.Post(new MetaRequest.Authenticate() {
          userId = userId.current, token = _token });
      } else MaybeConnect();
    };
    firebaseAuth.IdTokenChanged += (source, args) => updateUserIdToken();
    updateUserIdToken();
  }

  private void Unresolve (DObject obj) {
    _objectsByPath.Remove(obj.path);
    if (obj.backing == BackingType.Server) {
      if (connected) rootObject.metaq.Post(new MetaRequest.Unsubscribe() { id = obj.id });
      _objectsById.Remove(obj.id);
      _recyclableIds.Add(obj.id);
      MaybeDisconnect();
    }
  }

  private void MaybeConnect () {
    // we connect if authed and we have a server-backed object other than the root
    if (!connected && userId.current != null && _objectsById.Count > 1) Connect();
  }

  private async void MaybeDisconnect () {
    if (connected && _objectsById.Count == 1) {
      _reconnect = false;
      await _webSocket.Close();
    }
  }

  private async void Connect () {
    _webSocket = new WebSocket(_url);
    _reconnect = true;
    _webSocket.OnOpen += () => {
      Debug.Log("Connected to " + _url);
      _reconnectAttempts = 0;

      // authenticate
      rootObject.metaq.Post(new MetaRequest.Authenticate() {
        userId = userId.current, token = _token });

      // subscribe
      foreach (var pair in _objectsById) {
        var obj = (DObject)pair.Value.Target;
        if (obj == null || obj == rootObject) continue;
        rootObject.metaq.Post(new MetaRequest.Subscribe() { id = pair.Key, path = obj.path });
      }
    };
    _webSocket.OnError += error => Debug.LogError(error);
    _webSocket.OnClose += error => {
      Debug.Log("Closed connection.");
      // notify all objects
      foreach (var reference in _objectsById.Values) ((DObject)reference.Target)?.OnDisconnect();
      if (!_reconnect) return;
      var seconds = (int)Math.Pow(2, Math.Min(_reconnectAttempts++, 9)); // max out at ~10 mins
      Debug.Log($"Reconnect attempt #{_reconnectAttempts} in {seconds}s.");
      Task.Delay(seconds * 1000).ContinueWith(task => Connect());
    };
    _webSocket.OnMessage += bytes => {
      using (var decoder = new Decoder(bytes)) {
        var id = decoder.ReadVarUInt();
        WeakReference reference;
        if (_objectsById.TryGetValue(id, out reference)) {
          ((DObject)reference.Target)?.ClientDecode(decoder);
        } else Debug.LogWarning($"Received message for unknown object {id}.");
      }
    };

    await _webSocket.Connect();
  }

  private void OnMessageGenerated (object source, byte[] message) {
    _webSocket.Send(message);
  }

  private void OnMetaQueueReceive (object source, MetaResponse response) {
    if (response is MetaResponse.SubscribeFailed) {
      var subscribeFailed = (MetaResponse.SubscribeFailed)response;
      Debug.LogWarning(
        $"Failed to subscribe [id={subscribeFailed.id}, cause={subscribeFailed.cause}].");
    } else {
      Debug.LogWarning($"Unknown meta-response type [response={response}].");
    }
  }

  private static CollectionFields GetCollectionFields (Type type) {
    CollectionFields fields;
    if (_collectionFields.TryGetValue(type, out fields)) return fields;
    _collectionFields.Add(type, fields = new CollectionFields());
    foreach (var fieldInfo in type.GetFields()) {
      var idAttributes = (Id[])fieldInfo.GetCustomAttributes(typeof(Id), false);
      if (idAttributes.Length == 0) continue;
      var fieldType = fieldInfo.FieldType;
      if (!(
        fieldType.IsGenericType &&
        fieldType.GetGenericTypeDefinition() == typeof(DCollection<>)
      )) continue;
      fields.Add(idAttributes[0].value, (fieldInfo.Name, fieldType.GetGenericArguments()[0]));
    }
    return fields;
  }

  private class CollectionFields : Dictionary<uint, (string name, Type type)> {}

  private static Dictionary<Type, CollectionFields> _collectionFields =
    new Dictionary<Type, CollectionFields>();

  private string _url;
  private string _token;
  private WebSocket _webSocket;

  private bool _reconnect = true;
  private int _reconnectAttempts = 0;

  private readonly Mutable<string> _userId = Mutable<string>.Local(null);
  private Dictionary<uint, WeakReference> _objectsById = new Dictionary<uint, WeakReference>();
  private Dictionary<Path, WeakReference> _objectsByPath = new Dictionary<Path, WeakReference>();
  private uint _nextId;
  private Heap<uint> _recyclableIds = new Heap<uint>();
}

}
