namespace GGFolks.Protocol {

using System;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;

/// <summary>
/// Represents a hash set property in a distributed object.
/// </summary>
public class DSet<T> : DProperty {

  /// <summary>
  /// An event fired when an element is added to the set.
  /// </summary>
  public event EventHandler<T> added;

  /// <summary>
  /// An event fired when an element is removed from the set.
  /// </summary>
  public event EventHandler<T> removed;

  /// <summary>
  /// Adds an element to the set.  Can only be called on the server.
  /// </summary>
  /// <returns>True if the element was added, false if it was already present.</returns>
  public bool Add (T item) {
    RequireServerOrFirestore();
    if (!_set.Add(item)) return false;
    added?.Invoke(this, item);
    _owner.OnSetAdd(this, item);
    return true;
  }

  /// <summary>
  /// Removes an element from the set.  Can only be called on the server.
  /// </summary>
  /// <returns>True if the element was removed, false if it was not present.</returns>
  public bool Remove (T item) {
    RequireServerOrFirestore();
    if (!_set.Remove(item)) return false;
    removed?.Invoke(this, item);
    _owner.OnSetRemove(this, item);
    return true;
  }

  /// <summary>
  /// Encodes a single value.
  /// </summary>
  public void EncodeValue (Encoder encoder, T value) {
    encoder.WriteVarUInt(_valueIdWireType);
    _valueWriter(encoder, value);
  }

  public override void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    base.Init(owner, name, id, ctx, backing);

    var setType = typeof(HashSet<T>);
    _setIdWireType = Encoder.EncodeIdWireType(id, setType);
    _setWriter = Encoder.GetValueWriter(setType);
    _setReader = Decoder.GetValueReader(setType);

    var valueType = typeof(T);
    _valueIdWireType = Encoder.EncodeIdWireType(id, valueType);
    _valueWriter = Encoder.GetValueWriter(valueType);
    _valueReader = Decoder.GetValueReader(valueType);
  }

  public override void Encode (Encoder encoder) {
    encoder.WriteVarUInt(_setIdWireType);
    _setWriter(encoder, _set);
  }

  public override void Decode (Decoder decoder, WireType wireType) {
    var newSet = (HashSet<T>)_setReader(decoder, wireType, _ctx);

    // remove anything not in the new set
    var oldItems = _set.ToArray();
    foreach (var item in oldItems) {
      if (newSet == null || !newSet.Contains(item)) {
        _set.Remove(item);
        removed?.Invoke(this, item);
      }
    }

    // add anything not in the old set
    if (newSet != null) {
      foreach (var item in newSet) {
        if (_set.Add(item)) added?.Invoke(this, item);
      }
    }
  }

  public override void DecodeSetAdd (Decoder decoder, WireType wireType) {
    var item = (T)_valueReader(decoder, wireType, _ctx);
    if (_set.Add(item)) added?.Invoke(this, item);
  }

  public override void DecodeSetRemove (Decoder decoder, WireType wireType) {
    var item = (T)_valueReader(decoder, wireType, _ctx);
    if (_set.Remove(item)) removed?.Invoke(this, item);
  }

  public override void Extract (DocumentSnapshot snapshot) {
    Dictionary<string, object> value;
    if (!snapshot.TryGetValue(firestoreField, out value)) return;

    // remove anything not in the new set
    var oldItems = _set.ToArray();
    foreach (var item in oldItems) {
      if (value == null || !value.ContainsKey(item.ToString())) {
        _set.Remove(item);
        removed?.Invoke(this, item);
      }
    }

    // add anything not in the old set
    if (value != null) {
      foreach (var key in value.Keys) {
        var newItem = (T)Convert.ChangeType(key, typeof(T));
        if (_set.Add(newItem)) added?.Invoke(this, newItem);
      }
    }
  }

  private HashSet<T> _set = new HashSet<T>();

  private uint _setIdWireType;
  private Encoder.ValueWriter _setWriter;
  private Decoder.ValueReader _setReader;

  private uint _valueIdWireType;
  private Encoder.ValueWriter _valueWriter;
  private Decoder.ValueReader _valueReader;
}

}
