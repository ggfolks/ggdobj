namespace GGFolks.Protocol {

using System;

/// <summary>
/// Represents a queue field in a distributed object with separate types for upstream (client to
/// server) and downstream (server to client) messages.
/// </summary>
public class DQueue<TUp, TDown> : DField {

  /// <summary>
  /// An event fired when a message is posted on the queue.
  /// </summary>
  public event EventHandler<(TUp, ISubscriber)> posted;

  /// <summary>
  /// An event fired when a message is received on the client's queue.
  /// </summary>
  public event EventHandler<TDown> received;

  /// <summary>
  /// Posts a message from the client to the server.  Can only be called on the client.
  /// </summary>
  public void Post (TUp message) {
    RequireClient();
    if (_owner.backing == BackingType.Firestore) posted?.Invoke(this, (message, _owner.client));
    else _owner.OnPost(this, message);
  }

  /// <summary>
  /// Broadcasts a message from the server to all clients.  Can only be called on the server.
  /// </summary>
  public void Broadcast (TDown message) {
    RequireServer();
    _owner.OnBroadcast(this, message);
  }

  /// <summary>
  /// Sends a message from the server to a single client.  Can only be called on the server.
  /// </summary>
  public void Send (TDown message, ISession session) {
    RequireServer();
    _owner.OnSend(this, message, session);
  }

  /// <summary>
  /// Encodes a single upstream message.
  /// </summary>
  public void EncodeUp (Encoder encoder, TUp message) {
    encoder.WriteVarUInt(_upIdWireType);
    _upWriter(encoder, message);
  }

  /// <summary>
  /// Encodes a single downstream message.
  /// </summary>
  public void EncodeDown (Encoder encoder, TDown message) {
    encoder.WriteVarUInt(_downIdWireType);
    _downWriter(encoder, message);
  }

  public override void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    base.Init(owner, name, id, ctx, backing);

    var upType = typeof(TUp);
    _upIdWireType = Encoder.EncodeIdWireType(id, upType);
    _upWriter = Encoder.GetValueWriter(upType);
    _upReader = Decoder.GetValueReader(upType);

    var downType = typeof(TDown);
    _downIdWireType = Encoder.EncodeIdWireType(id, downType);
    _downWriter = Encoder.GetValueWriter(downType);
    _downReader = Decoder.GetValueReader(downType);
  }

  public override void DecodeQueuePost (Decoder decoder, WireType wireType, ISession session) {
    var message = (TUp)_upReader(decoder, wireType, _ctx);
    posted?.Invoke(this, (message, session));
  }

  public override void DecodeQueueReceive (Decoder decoder, WireType wireType) {
    var message = (TDown)_downReader(decoder, wireType, _ctx);
    received?.Invoke(this, message);
  }

  private uint _upIdWireType;
  private Encoder.ValueWriter _upWriter;
  private Decoder.ValueReader _upReader;

  private uint _downIdWireType;
  private Encoder.ValueWriter _downWriter;
  private Decoder.ValueReader _downReader;
}

}
