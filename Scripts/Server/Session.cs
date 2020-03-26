namespace GGFolks.Server {

using System;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;
﻿using WebSocketSharp.Server;

using Data;
using Protocol;
using React;

/// <summary>
/// Handles a single websocket session.
/// </summary>
public class Session<TRoot> : WebSocketBehavior, ISession where TRoot : AbstractRootObject, new() {

  // defined by ISubscriber
  public Value<string> userId { get; } = new Value<string>();

  // defined by ISession
  public void SubscribeToObject (uint id, DObject obj) {
    _objectsById.Add(id, obj);
    _idsByObject.Add(obj, id);
    obj.messageGenerated += OnMessageGenerated;

    // send the object's current state to the client
    _encoder.WriteVarUInt(id);
    obj.ServerEncode(_encoder);
    EnqueueToSend(_encoder.Finish());

    // let any listeners know we've subscribed
    obj.OnSubscribe(this);
  }

  // defined by ISession
  public void UnsubscribeFromObject (uint id) {
    DObject obj;
    if (!_objectsById.TryGetValue(id, out obj)) {
      Debug.LogWarning($"Missing object to unsubscribe from [who={this}, id={id}].");
      return;
    }
    _objectsById.Remove(id);
    _idsByObject.Remove(obj);
    obj.messageGenerated -= OnMessageGenerated;
    obj.OnUnsubscribe(this);
  }

  // defined by ISession
  public void Send (DObject obj, byte[] data) {
    uint id;
    if (!_idsByObject.TryGetValue(obj, out id)) {
      Debug.LogWarning($"Not subscribed to object [who={this}, obj={obj}].");
      return;
    }
    _encoder.WriteVarUInt(id);
    _encoder.Write(data);
    EnqueueToSend(_encoder.Finish());
  }

  // inherited from Object
  public override string ToString () {
    return $"[{_userEndPoint}{(userId.current == null ? "" : $", {userId.current}")}]";
  }

  protected override void OnOpen () {
    // store the endpoint string; we can't access it after close
    _userEndPoint = Context.UserEndPoint.ToString();

    // notify the main thread
    Server<TRoot>.synchronizationContext.Post(MainThreadOnOpen, null);
  }

  protected override void OnClose (CloseEventArgs args) {
    Server<TRoot>.synchronizationContext.Post(MainThreadOnClose, null);
  }

  protected override void OnMessage (MessageEventArgs args) {
    Server<TRoot>.synchronizationContext.Post(MainThreadOnMessage, args.RawData);
  }

  private void MainThreadOnOpen (object data) {
    Debug.Log($"Client connected [who={this}].");
    SubscribeToObject(0, Server<TRoot>.rootObject);
  }

  private void MainThreadOnClose (object data) {
    foreach (var obj in _objectsById.Values) {
      obj.messageGenerated -= OnMessageGenerated;
      obj.OnUnsubscribe(this);
    }
    Debug.Log($"Client disconnected [who={this}].");
  }

  private void OnMessageGenerated (object source, byte[] data) {
    Send((DObject)source, data);
  }

  private void MainThreadOnMessage (object data) {
    using (var decoder = new Decoder((byte[])data)) {
      var id = decoder.ReadVarUInt();
      DObject obj;
      if (_objectsById.TryGetValue(id, out obj)) obj.ServerDecode(decoder, this);
      else Debug.LogWarning($"Received message for unknown object [who={this}, id={id}].");
    }
  }

  private void EnqueueToSend (byte[] data) {
    if (_sending) _sendQueue.Enqueue(data);
    else {
      _sending = true;
      SendAsync(data, SendCompleted);
    }
  }

  private void SendCompleted (bool success) {
    if (_sendQueue.Count > 0) {
      SendAsync(_sendQueue.Dequeue(), SendCompleted);
    } else {
      _sending = false;
    }
  }

  private Dictionary<uint, DObject> _objectsById = new Dictionary<uint, DObject>();
  private Dictionary<DObject, uint> _idsByObject = new Dictionary<DObject, uint>();
  private Encoder _encoder = new Encoder();
  private string _userEndPoint;
  private bool _sending;
  private Queue<byte[]> _sendQueue = new Queue<byte[]>();
}

}
