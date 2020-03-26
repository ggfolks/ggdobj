namespace GGFolks.Protocol {

using System;
using System.Collections.Generic;
using System.Linq;
using Firebase.Firestore;

/// <summary>
/// Represents a hash map field in a distributed object.
/// </summary>
public class DMap<TKey, TValue> : DField {

  /// <summary>
  /// An event fired when an entry is set within the set.
  /// </summary>
  public event EventHandler<KeyValuePair<TKey, TValue>> set;

  /// <summary>
  /// An event fired when an entry is removed from the set.
  /// </summary>
  public event EventHandler<TKey> removed;

  /// <summary>
  /// Sets an entry in the map.  Can only be called on the server.
  /// </summary>
  public void Set (TKey key, TValue value) {
    RequireServerOrFirestore();
    _dictionary[key] = value;
    set?.Invoke(this, new KeyValuePair<TKey, TValue>(key, value));
    _owner.OnMapSet(this, key, value);
  }

  /// <summary>
  /// Removes an entry from the map.  Can only be called on the server.
  /// </summary>
  /// <returns>True if the entry was removed, false if the key was not present.</returns>
  public bool Remove (TKey key) {
    RequireServerOrFirestore();
    if (!_dictionary.Remove(key)) return false;
    removed?.Invoke(this, key);
    _owner.OnMapRemove(this, key);
    return true;
  }

  /// <summary>
  /// Encodes a single key.
  /// </summary>
  public void EncodeKey (Encoder encoder, TKey key) {
    encoder.WriteVarUInt(_keyIdWireType);
    _keyWriter(encoder, key);
  }

  /// <summary>
  /// Encodes a single key/value pair.
  /// </summary>
  public void EncodeKeyValue (Encoder encoder, TKey key, TValue value) {
    encoder.WriteVarUInt(_keyValueIdWireTypes);
    _keyWriter(encoder, key);
    _valueWriter(encoder, value);
  }

  /// <summary>
  /// Converts a single value to the Firestore equivalent.
  /// </summary>
  public object ConvertValueToFirestore (TValue value) {
    return _converterToFirestore(value);
  }

  public override void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    base.Init(owner, name, id, ctx, backing);

    var dictionaryType = typeof(Dictionary<TKey, TValue>);
    _dictionaryIdWireType = Encoder.EncodeIdWireType(id, dictionaryType);
    _dictionaryWriter = Encoder.GetValueWriter(dictionaryType);
    _dictionaryReader = Decoder.GetValueReader(dictionaryType);

    var keyType = typeof(TKey);
    _keyIdWireType = Encoder.EncodeIdWireType(id, keyType);
    _keyWriter = Encoder.GetValueWriter(keyType);
    _keyReader = Decoder.GetValueReader(keyType);

    var valueType = typeof(TValue);
    _keyValueIdWireTypes = Encoder.EncodeIdWireTypes(id, keyType, valueType);
    _valueWriter = Encoder.GetValueWriter(valueType);
    _valueReader = Decoder.GetValueReader(valueType);

    _converterToFirestore = TypeUtil.GetConverterToFirestore(valueType);
    _converterFromFirestore = TypeUtil.GetConverterFromFirestore(valueType);
  }

  public override void Encode (Encoder encoder) {
    encoder.WriteVarUInt(_dictionaryIdWireType);
    _dictionaryWriter(encoder, _dictionary);
  }

  public override void Decode (Decoder decoder, WireType wireType) {
    var newDictionary = (Dictionary<TKey, TValue>)_dictionaryReader(decoder, wireType, _ctx);

    // remove anything not in the new dictionary
    var oldPairs = _dictionary.ToArray();
    foreach (var pair in oldPairs) {
      if (newDictionary == null || !newDictionary.ContainsKey(pair.Key)) {
        _dictionary.Remove(pair.Key);
        removed?.Invoke(this, pair.Key);
      }
    }

    // add anything not in the old dictionary
    if (newDictionary != null) {
      foreach (var pair in newDictionary) {
        TValue oldValue;
        if (!(
          _dictionary.TryGetValue(pair.Key, out oldValue) &&
          Object.Equals(oldValue, pair.Value)
        )) {
          _dictionary[pair.Key] = pair.Value;
          set?.Invoke(this, pair);
        }
      }
    }
  }

  public override void DecodeMapSet (Decoder decoder, WireType keyType, WireType valueType) {
    var key = (TKey)_keyReader(decoder, keyType, _ctx);
    var value = (TValue)_valueReader(decoder, valueType, _ctx);
    TValue oldValue;
    if (!(_dictionary.TryGetValue(key, out oldValue) && Object.Equals(oldValue, value))) {
      _dictionary[key] = value;
      set?.Invoke(this, new KeyValuePair<TKey, TValue>(key, value));
    }
  }

  public override void DecodeMapRemove (Decoder decoder, WireType wireType) {
    var key = (TKey)_keyReader(decoder, wireType, _ctx);
    if (_dictionary.Remove(key)) removed?.Invoke(this, key);
  }

  public override void Extract (DocumentSnapshot snapshot) {
    Dictionary<string, object> value;
    if (!snapshot.TryGetValue(firestoreField, out value)) return;

    // remove anything not in the new dictionary
    var oldPairs = _dictionary.ToArray();
    foreach (var pair in oldPairs) {
      if (value == null || !value.ContainsKey(pair.Key.ToString())) {
        _dictionary.Remove(pair.Key);
        removed?.Invoke(this, pair.Key);
      }
    }

    // add anything not in the old dictionary
    if (value != null) {
      foreach (var pair in value) {
        var newKey = (TKey)Convert.ChangeType(pair.Key, typeof(TKey));
        var newValue = (TValue)_converterFromFirestore(pair.Value);
        TValue oldValue;
        if (!(
          _dictionary.TryGetValue(newKey, out oldValue) &&
          Object.Equals(oldValue, newValue)
        )) {
          _dictionary[newKey] = newValue;
          set?.Invoke(this, new KeyValuePair<TKey, TValue>(newKey, newValue));
        }
      }
    }
  }

  private Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();

  private uint _dictionaryIdWireType;
  private Encoder.ValueWriter _dictionaryWriter;
  private Decoder.ValueReader _dictionaryReader;

  private uint _keyIdWireType;
  private Encoder.ValueWriter _keyWriter;
  private Decoder.ValueReader _keyReader;

  private uint _keyValueIdWireTypes;
  private Encoder.ValueWriter _valueWriter;
  private Decoder.ValueReader _valueReader;

  private TypeUtil.FirestoreConverter _converterToFirestore;
  private TypeUtil.FirestoreConverter _converterFromFirestore;
}

}
