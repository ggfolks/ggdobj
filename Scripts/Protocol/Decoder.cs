namespace GGFolks.Protocol {

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Decodes data from a byte array (e.g., a websocket message).
/// </summary>
public class Decoder : BinaryReader {

  /// <summary>
  /// Delegate type for methods that read values of a known type from an encoder (with size
  /// information, but without metadata such as the wire type).
  /// </summary>
  /// <param name="decoder">The decoder to read from.</param>
  /// <param name="wireType">The wire type of the value to read.</param>
  /// <param name="ctx">Context information to include in error messages.</param>
  public delegate object ValueReader (Decoder decoder, WireType wireType, object ctx);

  /// <summary>
  /// Delegate type for methods that read values of a known type from an encoder (without size
  /// information).
  /// </summary>
  public delegate object TypeReader (Decoder decoder, long end);

  /// <summary>
  /// Gets a value reader for the identified type.
  /// </summary>
  public static ValueReader GetValueReader (Type type) {
    if (type.IsEnum) return _valueReaders[type.GetEnumUnderlyingType()];
    ValueReader valueReader;
    if (_valueReaders.TryGetValue(type, out valueReader)) return valueReader;
    if (type.IsValueType) {
      if (type.IsGenericType && TypeUtil.tupleTypes.Contains(type.GetGenericTypeDefinition())) {
        var tupleArgs = type.GetGenericArguments();
        var fields = new (FieldInfo info, ValueReader reader)[tupleArgs.Length];
        for (var ii = 0; ii < fields.Length; ii++) {
          var info = type.GetField($"Item{ii + 1}");
          fields[ii] = (info, GetValueReader(info.FieldType));
        }
        _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
          var value = Activator.CreateInstance(type);
          if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) return value;
          var length = decoder.ReadVarUInt();
          if (length == 0) {
            Debug.LogWarning($"Tuple encoded as zero-length [ctx={ctx}].");
            return value;
          }
          var end = decoder.BaseStream.Position + length;
          var wireTypes = decoder.ReadVarUInt();
          var shift = 2 * (fields.Length - 1);
          foreach (var field in fields) {
            var itemWireType = (WireType)(wireTypes >> shift & 3);
            field.info.SetValue(value, field.reader(decoder, itemWireType, ctx));
            shift -= 2;
          }
          if (decoder.BaseStream.Position != end) {
            Debug.LogWarning($"Tuple length mismatch [ctx={ctx}].");
            decoder.BaseStream.Seek(end, SeekOrigin.Begin);
          }
          return value;
        });
        return valueReader;
      }
      var typeReader = GetTypeReader(type);
      _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
        if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) {
          return Activator.CreateInstance(type);
        }
        var length = decoder.ReadVarUInt();
        return typeReader(decoder, decoder.BaseStream.Position + length);
      });
      return valueReader;
    }
    if (type.IsArray) {
      var elementType = type.GetElementType();
      var elementReader = GetValueReader(elementType);
      _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
        if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) return null;
        var length = decoder.ReadVarUInt();
        if (length == 0) {
          Debug.LogWarning($"Array encoded as zero-length [ctx={ctx}].");
          return null;
        }
        var end = decoder.BaseStream.Position + length;
        var (id, elementWireType) = DecodeIdWireType(decoder.ReadVarUInt());
        if (id == 0) {
          if (decoder.BaseStream.Position < end) {
            Debug.LogWarning($"Extra data included for null array [ctx={ctx}].");
            decoder.BaseStream.Seek(end, SeekOrigin.Begin);
          }
          return null;
        }
        var list = new List<object>();
        while (decoder.BaseStream.Position < end) {
          list.Add(elementReader(decoder, elementWireType, ctx));
        }
        var array = Array.CreateInstance(elementType, list.Count);
        for (var ii = 0; ii < array.Length; ii++) array.SetValue(list[ii], ii);
        return array;
      });
      return valueReader;
    }
    var dictionaryInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(IDictionary<,>));
    if (dictionaryInterfaces.Length > 0) {
      var dictionaryArgs = dictionaryInterfaces[0].GetGenericArguments();
      var dictionaryKeyReader = GetValueReader(dictionaryArgs[0]);
      var dictionaryValueReader = GetValueReader(dictionaryArgs[1]);
      var constructor = type.GetConstructor(Type.EmptyTypes);
      var addMethod = type.GetMethod("Add", dictionaryArgs);
      _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
        if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) return null;
        var length = decoder.ReadVarUInt();
        if (length == 0) {
          Debug.LogWarning($"Dictionary encoded as zero-length [ctx={ctx}].");
          return null;
        }
        var end = decoder.BaseStream.Position + length;
        var (id, keyWireType, valueWireType) = DecodeIdWireTypes(decoder.ReadVarUInt());
        if (id == 0) {
          if (decoder.BaseStream.Position < end) {
            Debug.LogWarning($"Extra data included for null dictionary [ctx={ctx}].");
            decoder.BaseStream.Seek(end, SeekOrigin.Begin);
          }
          return null;
        }
        var dictionary = constructor.Invoke(null);
        var args = new object[2];
        while (decoder.BaseStream.Position < end) {
          args[0] = dictionaryKeyReader(decoder, keyWireType, ctx);
          args[1] = dictionaryValueReader(decoder, valueWireType, ctx);
          addMethod.Invoke(dictionary, args);
        }
        return dictionary;
      });
      return valueReader;
    }
    var collectionInterfaces = type.FindInterfaces(
      TypeUtil.MatchesGenericType, typeof(ICollection<>));
    if (collectionInterfaces.Length > 0) {
      var elementType = collectionInterfaces[0].GetGenericArguments()[0];
      var elementReader = GetValueReader(elementType);
      var constructor = type.GetConstructor(Type.EmptyTypes);
      var addMethod = type.GetMethod("Add", new [] { elementType });
      _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
        if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) return null;
        var length = decoder.ReadVarUInt();
        if (length == 0) {
          Debug.LogWarning($"Collection encoded as zero-length [ctx={ctx}].");
          return null;
        }
        var end = decoder.BaseStream.Position + length;
        var (id, elementWireType) = DecodeIdWireType(decoder.ReadVarUInt());
        if (id == 0) {
          if (decoder.BaseStream.Position < end) {
            Debug.LogWarning($"Extra data included for null collection [ctx={ctx}].");
            decoder.BaseStream.Seek(end, SeekOrigin.Begin);
          }
          return null;
        }
        var collection = constructor.Invoke(null);
        var args = new object[1];
        while (decoder.BaseStream.Position < end) {
          args[0] = elementReader(decoder, elementWireType, ctx);
          addMethod.Invoke(collection, args);
        }
        return collection;
      });
    } else {
      var typeReader = GetTypeReader(type);
      _valueReaders.Add(type, valueReader = (decoder, wireType, ctx) => {
        if (!decoder.WireTypesMatch(WireType.ByteLength, wireType, ctx)) return null;
        var length = decoder.ReadVarUInt();
        return typeReader(decoder, decoder.BaseStream.Position + length);
      });
    }
    return valueReader;
  }

  /// <summary>
  /// Gets a type reader for the identified type (which must be a struct or class).
  /// </summary>
  public static TypeReader GetTypeReader (Type type) {
    TypeReader typeReader;
    if (!_typeReaders.TryGetValue(type, out typeReader)) {
      if (type.IsValueType) {
        _typeReaders.Add(type, typeReader = CreateBasicTypeReader(type));

      } else {
        // if we have subtype attributes, we want to read the type id and get a subtype reader
        // based on that
        var subtypeAttributes = (Subtypes[])type.GetCustomAttributes(typeof(Subtypes), false);
        if (subtypeAttributes.Length > 0) {
          var subtypeReaders = new Dictionary<uint, TypeReader>();
          foreach (Type subtype in subtypeAttributes[0].value) {
            var idAttributes = (Id[])subtype.GetCustomAttributes(typeof(Id), false);
            if (idAttributes.Length == 0) continue;
            subtypeReaders.Add(idAttributes[0].value, GetTypeReader(subtype));
          }
          _typeReaders.Add(type, typeReader = (decoder, end) => {
            var typeId = decoder.ReadVarUInt();
            if (typeId == 0) return null;
            TypeReader subtypeReader;
            if (subtypeReaders.TryGetValue(typeId, out subtypeReader)) {
              return subtypeReader(decoder, end);
            }
            Debug.LogWarning($"Skipping unknown subtype [typeId={typeId}, type={type}].");
            decoder.BaseStream.Seek(end, SeekOrigin.Begin);
            return null;
          });
        } else {
          // if we have an id, assume that we're the subtype
          var basicReader = CreateBasicTypeReader(type);
          var idAttributes = (Id[])type.GetCustomAttributes(typeof(Id), false);
          if (idAttributes.Length > 0) {
            _typeReaders.Add(type, typeReader = basicReader);
          } else {
            // with neither subtypes nor id, we assume a simple class (like a struct, but nullable)
            _typeReaders.Add(type, typeReader = (decoder, end) => {
              var typeId = decoder.ReadVarUInt();
              if (typeId == 0) return null;
              return basicReader(decoder, end);
            });
          }
        }
      }
    }
    return typeReader;
  }

  /// <summary>
  /// Decodes a combined field id and wire type.
  /// </summary>
  public static (uint, WireType) DecodeIdWireType (uint idWireType) {
    return (idWireType >> 2, (WireType)(idWireType & 3));
  }

  /// <summary>
  /// Decodes a combined field id, key wire type, and value wire type.
  /// </summary>
  public static (uint, WireType, WireType) DecodeIdWireTypes (uint idWireTypes) {
    return (idWireTypes >> 4, (WireType)(idWireTypes >> 2 & 3), (WireType)(idWireTypes & 3));
  }

  public Decoder (byte[] source) : base(new MemoryStream(source)) {}

  /// <summary>
  /// Reads a signed integer with variable-length ZigZag encoding.
  /// </summary>
  public int ReadVarInt () {
    // https://golb.hplar.ch/2019/06/variable-length-int-java.html#zigzag-decoder
    var value = Read7BitEncodedInt();
    return (int)((uint)value >> 1) ^ -(value & 1);
  }

  /// <summary>
  /// Reads an unsigned integer with variable-length encoding.
  /// </summary>
  public uint ReadVarUInt () {
    return (uint)Read7BitEncodedInt();
  }

  /// <summary>
  /// Skips a value with the supplied wire type.
  /// </summary>
  public void Skip (WireType wireType) {
    switch (wireType) {
      case WireType.VarInt:
        ReadVarUInt();
        break;

      case WireType.FourByte:
        BaseStream.Seek(4, SeekOrigin.Current);
        break;

      case WireType.EightByte:
        BaseStream.Seek(8, SeekOrigin.Current);
        break;

      case WireType.ByteLength:
        BaseStream.Seek(ReadVarUInt(), SeekOrigin.Current);
        break;
    }
  }

  private int ReadVarInt (WireType wireType, object ctx) {
    return WireTypesMatch(WireType.VarInt, wireType, ctx) ? ReadVarInt() : 0;
  }

  private uint ReadVarUInt (WireType wireType, object ctx) {
    return WireTypesMatch(WireType.VarInt, wireType, ctx) ? ReadVarUInt() : 0;
  }

  private float ReadSingle (WireType wireType, object ctx) {
    return WireTypesMatch(WireType.FourByte, wireType, ctx) ? ReadSingle() : 0f;
  }

  private double ReadDouble (WireType wireType, object ctx) {
    return WireTypesMatch(WireType.EightByte, wireType, ctx) ? ReadDouble() : 0.0;
  }

  private string ReadString (WireType wireType, object ctx) {
    return WireTypesMatch(WireType.ByteLength, wireType, ctx) ? ReadString() : "";
  }

  private Vector3 ReadVector3 (WireType wireType, object ctx) {
    if (!WireTypesMatch(WireType.ByteLength, wireType, ctx)) return Vector3.zero;
    var length = ReadVarUInt();
    if (length != 12) {
      Debug.LogWarning($"Invalid length for Vector3 [ctx={ctx}, length={length}].");
      BaseStream.Seek(length, SeekOrigin.Current);
      return Vector3.zero;
    }
    return new Vector3(ReadSingle(), ReadSingle(), ReadSingle());
  }

  private Guid ReadGuid (WireType wireType, object ctx) {
    if (!WireTypesMatch(WireType.ByteLength, wireType, ctx)) return Guid.Empty;
    var length = ReadVarUInt();
    if (length != 16) {
      Debug.LogWarning($"Invalid length for Guid for [ctx={ctx}, length={length}].");
      BaseStream.Seek(length, SeekOrigin.Current);
      return Guid.Empty;
    }
    return new Guid(ReadBytes(16));
  }

  private bool WireTypesMatch (WireType wanted, WireType got, object ctx) {
    if (wanted == got) return true;
    Debug.LogWarning($"Wrong wire type [wanted={wanted}, got={got}, ctx={ctx}].");
    Skip(got);
    return false;
  }

  private static TypeReader CreateBasicTypeReader (Type type) {
    var fieldReaders = new Dictionary<uint, FieldReader>();
    foreach (var field in type.GetFields()) {
      var idAttributes = (Id[])field.GetCustomAttributes(typeof(Id), false);
      if (idAttributes.Length == 0) continue;
      var valueReader = GetValueReader(field.FieldType);
      var ctx = $"{type.FullName}.{field.Name}";
      fieldReaders.Add(idAttributes[0].value, (decoder, value, wireType) => {
        field.SetValue(value, valueReader(decoder, wireType, ctx));
      });
    }
    return (decoder, end) => {
      var value = Activator.CreateInstance(type);
      while (decoder.BaseStream.Position < end) {
        var (id, wireType) = DecodeIdWireType(decoder.ReadVarUInt());
        FieldReader fieldReader;
        if (fieldReaders.TryGetValue(id, out fieldReader)) fieldReader(decoder, value, wireType);
        else decoder.Skip(wireType); // unknown field; skip over
      }
      return value;
    };
  }

  private delegate void FieldReader (Decoder decoder, object value, WireType wireType);

  private static Dictionary<Type, TypeReader> _typeReaders = new Dictionary<Type, TypeReader>();

  private static Dictionary<Type, ValueReader> _valueReaders = new Dictionary<Type, ValueReader>() {
    { typeof(bool), (decoder, wireType, ctx) => decoder.ReadVarUInt(wireType, ctx) != 0 },
    { typeof(byte), (decoder, wireType, ctx) => (byte)decoder.ReadVarUInt(wireType, ctx) },
    { typeof(sbyte), (decoder, wireType, ctx) => (sbyte)decoder.ReadVarInt(wireType, ctx) },
    { typeof(char), (decoder, wireType, ctx) => (char)decoder.ReadVarUInt(wireType, ctx) },
    { typeof(ushort), (decoder, wireType, ctx) => (ushort)decoder.ReadVarUInt(wireType, ctx) },
    { typeof(short), (decoder, wireType, ctx) => (short)decoder.ReadVarInt(wireType, ctx) },
    { typeof(int), (decoder, wireType, ctx) => decoder.ReadVarInt(wireType, ctx) },
    { typeof(uint), (decoder, wireType, ctx) => decoder.ReadVarUInt(wireType, ctx) },
    { typeof(float), (decoder, wireType, ctx) => decoder.ReadSingle(wireType, ctx) },
    { typeof(double), (decoder, wireType, ctx) => decoder.ReadDouble(wireType, ctx) },
    { typeof(string), (decoder, wireType, ctx) => decoder.ReadString(wireType, ctx) },
    { typeof(Vector3), (decoder, wireType, ctx) => decoder.ReadVector3(wireType, ctx) },
    { typeof(Guid), (decoder, wireType, ctx) => decoder.ReadGuid(wireType, ctx) },
  };
}

}
