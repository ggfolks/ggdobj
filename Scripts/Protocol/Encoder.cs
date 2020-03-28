namespace GGFolks.Protocol {

using System;
using System.Collections;
using System.Collections.Generic;
﻿using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Encodes data into a byte array (e.g., a websocket message).
/// </summary>
public class Encoder : BinaryWriter {

  /// <summary>
  /// Delegate type for methods that write values of a known type to an encoder (with size
  /// information, but without metadata such as the wire type).
  /// </summary>
  /// <param name="encoder">The encoder to write to.</param>
  /// <param name="value">The value to write.</param>
  public delegate void ValueWriter (Encoder encoder, object value);

  /// <summary>
  /// Delegate type for methods that write values of a known type to an encoder (without size
  /// information).
  /// </summary>
  public delegate void TypeWriter (Encoder encoder, object value);

  /// <summary>
  /// Gets a value writer for the identified type.
  /// </summary>
  public static ValueWriter GetValueWriter (Type type) {
    if (type.IsEnum) return _valueWriters[type.GetEnumUnderlyingType()];
    ValueWriter valueWriter;
    if (_valueWriters.TryGetValue(type, out valueWriter)) return valueWriter;
    if (type.IsValueType) {
      if (type.IsGenericType && TypeUtil.tupleTypes.Contains(type.GetGenericTypeDefinition())) {
        var tupleArgs = type.GetGenericArguments();
        var fields = new (FieldInfo info, ValueSizer sizer, ValueWriter writer)[tupleArgs.Length];
        var wireTypes = EncodeIdWireTypes(0, tupleArgs);
        var wireTypesSize = GetVarUIntSize(wireTypes);
        for (var ii = 0; ii < fields.Length; ii++) {
          var info = type.GetField($"Item{ii + 1}");
          fields[ii] = (info, GetValueSizer(info.FieldType), GetValueWriter(info.FieldType));
        }
        _valueWriters.Add(type, valueWriter = (encoder, value) => {
          var length = wireTypesSize;
          foreach (var field in fields) length += field.sizer(field.info.GetValue(value));
          encoder.WriteVarUInt(length);
          encoder.WriteVarUInt(wireTypes);
          foreach (var field in fields) field.writer(encoder, field.info.GetValue(value));
        });
        return valueWriter;
      }
      var typeSizer = GetTypeSizer(type);
      var typeWriter = GetTypeWriter(type);
      _valueWriters.Add(type, valueWriter = (encoder, value) => {
        encoder.WriteVarUInt(typeSizer(value));
        typeWriter(encoder, value);
      });
      return valueWriter;
    }
    if (type.IsArray) {
      _valueWriters.Add(type, valueWriter = CreateEnumerableWriter(type.GetElementType()));
      return valueWriter;
    }
    var dictionaryInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(IDictionary<,>));
    if (dictionaryInterfaces.Length > 0) {
      var dictionaryArgs = dictionaryInterfaces[0].GetGenericArguments();
      var dictionaryKeySizer = GetValueSizer(dictionaryArgs[0]);
      var dictionaryValueSizer = GetValueSizer(dictionaryArgs[1]);
      var dictionaryKeyWriter = GetValueWriter(dictionaryArgs[0]);
      var dictionaryValueWriter = GetValueWriter(dictionaryArgs[1]);
      var idWireTypes = EncodeIdWireTypes(1, dictionaryArgs[0], dictionaryArgs[1]);
      _valueWriters.Add(type, valueWriter = (encoder, value) => {
        if (value == null) {
          encoder.WriteVarUInt(1); // one byte in length
          encoder.WriteVarUInt(0); // "null"
          return;
        }
        var length = 1u; // one byte for element wire types + "true" (non-null)
        foreach (DictionaryEntry entry in (IDictionary)value) {
          length += dictionaryKeySizer(entry.Key);
          length += dictionaryValueSizer(entry.Value);
        }
        encoder.WriteVarUInt(length);
        encoder.WriteVarUInt(idWireTypes);
        foreach (DictionaryEntry entry in (IDictionary)value) {
          dictionaryKeyWriter(encoder, entry.Key);
          dictionaryValueWriter(encoder, entry.Value);
        }
      });
      return valueWriter;
    }
    var collectionInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(ICollection<>));
    if (collectionInterfaces.Length > 0) {
      var elementType = collectionInterfaces[0].GetGenericArguments()[0];
      _valueWriters.Add(type, valueWriter = CreateEnumerableWriter(elementType));
      return valueWriter;
    }
    _valueWriters.Add(type, valueWriter = (encoder, value) => {
      if (value == null) {
        encoder.WriteVarUInt(1); // one byte in length
        encoder.WriteVarUInt(0); // type is "null"
        return;
      }
      var subtype = value.GetType();
      encoder.WriteVarUInt(GetTypeSizer(subtype)(value));
      GetTypeWriter(subtype)(encoder, value);
    });
    return valueWriter;
  }

  /// <summary>
  /// Gets a type writer for the identified type (which must be a struct or class).
  /// </summary>
  public static TypeWriter GetTypeWriter (Type type) {
    TypeWriter typeWriter;
    if (!_typeWriters.TryGetValue(type, out typeWriter)) {
      var fieldWriterList = new List<TypeWriter>();
      foreach (var field in type.GetFields()) {
        var idAttributes = (Id[])field.GetCustomAttributes(typeof(Id), false);
        if (idAttributes.Length == 0) continue;
        var idWireType = EncodeIdWireType(idAttributes[0].value, field.FieldType);
        var valueWriter = GetValueWriter(field.FieldType);
        fieldWriterList.Add((encoder, value) => {
          encoder.WriteVarUInt(idWireType);
          valueWriter(encoder, field.GetValue(value));
        });
      }
      var fieldWriters = fieldWriterList.ToArray();
      if (type.IsValueType) {
        _typeWriters.Add(type, typeWriter = (encoder, value) => {
          foreach (var fieldWriter in fieldWriters) fieldWriter(encoder, value);
        });
      } else {
        uint id = 1; // default id is one, in case we don't bother with subclasses
        var idAttributes = (Id[])type.GetCustomAttributes(typeof(Id), false);
        if (idAttributes.Length > 0) id = idAttributes[0].value;
        _typeWriters.Add(type, typeWriter = (encoder, value) => {
          encoder.WriteVarUInt(id);
          foreach (var fieldWriter in fieldWriters) fieldWriter(encoder, value);
        });
      }
    }
    return typeWriter;
  }

  /// <summary>
  /// Encodes a field id along with the wire type corresponding to the type provided.
  /// </summary>
  public static uint EncodeIdWireType (uint id, Type type) {
    return id << 2 | (uint)GetWireType(type);
  }

  /// <summary>
  /// Encodes a field id along with key and value wire types.
  /// </summary>
  public static uint EncodeIdWireTypes (uint id, Type keyType, Type valueType) {
    return id << 4 | (uint)GetWireType(keyType) << 2 | (uint)GetWireType(valueType);
  }

  /// <summary>
  /// Encodes a field id along with multiple wire types.
  /// </summary>
  public static uint EncodeIdWireTypes (uint id, Type[] types) {
    var encoded = id;
    foreach (var type in types) encoded = encoded << 2 | (uint)GetWireType(type);
    return encoded;
  }

  public Encoder () : base(new MemoryStream()) {}

  /// <summary>
  /// Writes a signed integer with variable-length ZigZag encoding.
  /// </summary>
  public void WriteVarInt (int value) {
    Write7BitEncodedInt(ZigZagEncode(value));
  }

  /// <summary>
  /// Writes an unsigned integer with variable-length encoding.
  /// </summary>
  public void WriteVarUInt (uint value) {
    Write7BitEncodedInt((int)value);
  }

  /// <summary>
  /// Writes a Vector3 to the stream.
  /// </summary>
  public void Write (Vector3 value) {
    WriteVarUInt(12); // length
    Write(value.x);
    Write(value.y);
    Write(value.z);
  }

  /// <summary>
  /// Writes a GUID to the stream.
  /// </summary>
  public void Write (Guid value) {
    WriteVarUInt(16); // length
    Write(value.ToByteArray());
  }

  /// <summary>
  /// Returns the written data as a byte array and resets the underlying stream.
  /// </summary>
  public byte[] Finish () {
    var data = ((MemoryStream)OutStream).ToArray();
    BaseStream.Seek(0, SeekOrigin.Begin);
    BaseStream.SetLength(0);
    return data;
  }

  private static WireType GetWireType (Type type) {
    if (type == typeof(float)) return WireType.FourByte;
    if (type == typeof(double)) return WireType.EightByte;
    if (type.IsPrimitive || type.IsEnum) return WireType.VarInt;
    return WireType.ByteLength;
  }

  private static TypeSizer GetTypeSizer (Type type) {
    TypeSizer typeSizer;
    if (!_typeSizers.TryGetValue(type, out typeSizer)) {
      var fieldSizerList = new List<TypeSizer>();
      foreach (var field in type.GetFields()) {
        var idAttributes = (Id[])field.GetCustomAttributes(typeof(Id), false);
        if (idAttributes.Length == 0) continue;
        var idWireType = EncodeIdWireType(idAttributes[0].value, field.FieldType);
        var idWireTypeSize = GetVarUIntSize(idWireType);
        var valueSizer = GetValueSizer(field.FieldType);
        fieldSizerList.Add(value => idWireTypeSize + valueSizer(field.GetValue(value)));
      }
      var fieldSizers = fieldSizerList.ToArray();
      if (type.IsClass) {
        uint id = 1; // default id is one, in case we don't bother with subclasses
        var idAttributes = (Id[])type.GetCustomAttributes(typeof(Id), false);
        if (idAttributes.Length > 0) id = idAttributes[0].value;
        var idSize = GetVarUIntSize(id);
        _typeSizers.Add(type, typeSizer = value => {
          uint size = idSize;
          foreach (var fieldSizer in fieldSizers) size += fieldSizer(value);
          return size;
        });
      } else {
        _typeSizers.Add(type, typeSizer = value => {
          uint size = 0;
          foreach (var fieldSizer in fieldSizers) size += fieldSizer(value);
          return size;
        });
      }
    }
    return typeSizer;
  }

  private static ValueSizer GetValueSizer (Type type) {
    if (type.IsEnum) return _valueSizers[type.GetEnumUnderlyingType()];
    ValueSizer valueSizer;
    if (_valueSizers.TryGetValue(type, out valueSizer)) return valueSizer;
    if (type.IsValueType) {
      if (type.IsGenericType && TypeUtil.tupleTypes.Contains(type.GetGenericTypeDefinition())) {
        var tupleArgs = type.GetGenericArguments();
        var fields = new (FieldInfo info, ValueSizer sizer)[tupleArgs.Length];
        var wireTypesSize = GetVarUIntSize(EncodeIdWireTypes(0, tupleArgs));
        for (var ii = 0; ii < fields.Length; ii++) {
          var info = type.GetField($"Item{ii + 1}");
          fields[ii] = (info, GetValueSizer(info.FieldType));
        }
        _valueSizers.Add(type, valueSizer = value => {
          var byteCount = wireTypesSize;
          foreach (var field in fields) byteCount += field.sizer(field.info.GetValue(value));
          return GetVarUIntSize(byteCount) + byteCount;
        });
        return valueSizer;
      }
      var typeSizer = GetTypeSizer(type);
      _valueSizers.Add(type, valueSizer = value => {
        var byteCount = typeSizer(value);
        return GetVarUIntSize(byteCount) + byteCount;
      });
      return valueSizer;
    }
    if (type.IsArray) {
      _valueSizers.Add(type, valueSizer = CreateEnumerableSizer(type.GetElementType()));
      return valueSizer;
    }
    var dictionaryInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(IDictionary<,>));
    if (dictionaryInterfaces.Length > 0) {
      var dictionaryArgs = dictionaryInterfaces[0].GetGenericArguments();
      var dictionaryKeySizer = GetValueSizer(dictionaryArgs[0]);
      var dictionaryValueSizer = GetValueSizer(dictionaryArgs[1]);
      _valueSizers.Add(type, valueSizer = value => {
        if (value == null) return 2; // one byte for size, one for "null"
        var byteCount = 1u; // one byte for element wire types + "not null"
        foreach (DictionaryEntry entry in (IDictionary)value) {
          byteCount += dictionaryKeySizer(entry.Key);
          byteCount += dictionaryValueSizer(entry.Value);
        }
        return GetVarUIntSize(byteCount) + byteCount;
      });
      return valueSizer;
    }
    var collectionInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(ICollection<>));
    if (collectionInterfaces.Length > 0) {
      var elementType = collectionInterfaces[0].GetGenericArguments()[0];
      _valueSizers.Add(type, valueSizer = CreateEnumerableSizer(elementType));
      return valueSizer;
    }
    _valueSizers.Add(type, valueSizer = value => {
      if (value == null) return 2; // one byte for size, one for "null"
      var byteCount = GetTypeSizer(value.GetType())(value);
      return GetVarUIntSize(byteCount) + byteCount;
    });
    return valueSizer;
  }

  private static ValueSizer CreateEnumerableSizer (Type elementType) {
    var elementSizer = GetValueSizer(elementType);
    return value => {
      if (value == null) return 2; // one byte for size, one for "null"
      var byteCount = 1u; // one byte for element wire type + "not null"
      foreach (var element in (IEnumerable)value) byteCount += elementSizer(element);
      return GetVarUIntSize(byteCount) + byteCount;
    };
  }

  private static ValueWriter CreateEnumerableWriter (Type elementType) {
    var elementSizer = GetValueSizer(elementType);
    var elementWriter = GetValueWriter(elementType);
    var idWireType = EncodeIdWireType(1, elementType);
    return (encoder, value) => {
      if (value == null) {
        encoder.WriteVarUInt(1); // one byte in length
        encoder.WriteVarUInt(0); // "null"
        return;
      }
      var length = 1u; // one byte for element wire type + "true" (non-null)
      foreach (var element in (IEnumerable)value) length += elementSizer(element);
      encoder.WriteVarUInt(length);
      encoder.WriteVarUInt(idWireType);
      foreach (var element in (IEnumerable)value) elementWriter(encoder, element);
    };
  }

  private static uint GetVarIntSize (int value) {
    return GetVarUIntSize((uint)ZigZagEncode(value));
  }

  private static int ZigZagEncode (int value) {
    // https://golb.hplar.ch/2019/06/variable-length-int-java.html#zigzag-encoder
    return (value << 1) ^ (value >> 31);
  }

  private static uint GetVarUIntSize (uint value) {
    uint size = 0;
    do {
      size++;
      value >>= 7;
    } while (value > 0);
    return size;
  }

  private static uint GetStringSize (string value) {
    var byteCount = (uint)Encoding.UTF8.GetByteCount(value);
    return GetVarUIntSize(byteCount) + byteCount;
  }

  private delegate uint TypeSizer (object value);

  private delegate uint ValueSizer (object value);

  private static Dictionary<Type, TypeWriter> _typeWriters = new Dictionary<Type, TypeWriter>();

  private static Dictionary<Type, ValueWriter> _valueWriters = new Dictionary<Type, ValueWriter>() {
    { typeof(bool), (encoder, value) => encoder.Write((bool)value) },
    { typeof(byte), (encoder, value) => encoder.WriteVarUInt((byte)value) },
    { typeof(sbyte), (encoder, value) => encoder.WriteVarInt((sbyte)value) },
    { typeof(char), (encoder, value) => encoder.WriteVarUInt((char)value) },
    { typeof(ushort), (encoder, value) => encoder.WriteVarUInt((ushort)value) },
    { typeof(short), (encoder, value) => encoder.WriteVarInt((short)value) },
    { typeof(int), (encoder, value) => encoder.WriteVarInt((int)value) },
    { typeof(uint), (encoder, value) => encoder.WriteVarUInt((uint)value) },
    { typeof(float), (encoder, value) => encoder.Write((float)value) },
    { typeof(double), (encoder, value) => encoder.Write((double)value) },
    { typeof(string), (encoder, value) => encoder.Write((string)value) },
    { typeof(Vector3), (encoder, value) => encoder.Write((Vector3)value) },
    { typeof(Guid), (encoder, value) => encoder.Write((Guid)value) },
  };

  private static Dictionary<Type, TypeSizer> _typeSizers = new Dictionary<Type, TypeSizer>();

  private static Dictionary<Type, ValueSizer> _valueSizers = new Dictionary<Type, ValueSizer>() {
    { typeof(bool), value => 1 },
    { typeof(byte), value => GetVarUIntSize((byte)value) },
    { typeof(sbyte), value => GetVarIntSize((sbyte)value) },
    { typeof(char), value => GetVarUIntSize((char)value) },
    { typeof(ushort), value => GetVarUIntSize((ushort)value) },
    { typeof(short), value => GetVarIntSize((short)value) },
    { typeof(int), value => GetVarIntSize((int)value) },
    { typeof(uint), value => GetVarUIntSize((uint)value) },
    { typeof(float), value => 4 },
    { typeof(double), value => 8 },
    { typeof(string), value => GetStringSize((string)value) },
    { typeof(Vector3), value => 13 }, // one byte for length, 12 for components
    { typeof(Guid), value => 17 }, // one byte for length, 16 for data
  };
}

}
