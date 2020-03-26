namespace GGFolks.Protocol {

using System;
using Firebase.Firestore;

/// <summary>
/// Represents a simple value property in a distributed object.
/// </summary>
public class DValue<T> : DProperty {

  /// <summary>
  /// Retrieves or sets the current value of the property. Can only be set on the server.
  /// </summary>
  public T current {
    get => _current;
    set {
      RequireServerOrFirestore();
      if (Object.Equals(_current, value)) return;
      _current = value;
      changed?.Invoke(this, _current);
      _owner.OnValueChange(this);
    }
  }

  /// <summary>
  /// An event fired when the current value changes.
  /// </summary>
  public event EventHandler<T> changed;

  /// <summary>
  /// Converts a single value to the Firestore equivalent.
  /// </summary>
  public object ConvertToFirestore (T value) {
    return _converterToFirestore(value);
  }

  public override void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    base.Init(owner, name, id, ctx, backing);

    var type = typeof(T);
    _idWireType = Encoder.EncodeIdWireType(id, type);
    _valueWriter = Encoder.GetValueWriter(type);
    _valueReader = Decoder.GetValueReader(type);

    _converterToFirestore = TypeUtil.GetConverterToFirestore(type);
    _converterFromFirestore = TypeUtil.GetConverterFromFirestore(type);
  }

  public override void Encode (Encoder encoder) {
    encoder.WriteVarUInt(_idWireType);
    _valueWriter(encoder, _current);
  }

  public override void Decode (Decoder decoder, WireType wireType) {
    var value = _valueReader(decoder, wireType, _ctx);
    if (Object.Equals(_current, value)) return;
    _current = (T)value;
    changed?.Invoke(this, _current);
  }

  public override void Extract (DocumentSnapshot snapshot) {
    object value;
    if (!snapshot.TryGetValue(firestoreField, out value)) return;
    T newValue = (T)_converterFromFirestore(value);
    if (Object.Equals(_current, newValue)) return;
    _current = newValue;
    changed?.Invoke(this, _current);
  }

  private T _current;

  private uint _idWireType;
  private Encoder.ValueWriter _valueWriter;
  private Decoder.ValueReader _valueReader;

  private TypeUtil.FirestoreConverter _converterToFirestore;
  private TypeUtil.FirestoreConverter _converterFromFirestore;
}

}
