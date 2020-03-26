namespace GGFolks.Protocol {

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// General utilities for dealing with reflective types.
/// </summary>
public static class TypeUtil {

  /// <summary>
  /// Delegate type for firestore conversion (in either direction).
  /// </summary>
  public delegate object FirestoreConverter (object value);

  /// <summary>
  /// Returns a converter to turn values of the specified type into their Firestore equivalent.
  /// </summary>
  public static FirestoreConverter GetConverterToFirestore (Type type) {
    FirestoreConverter converter;
    if (_convertersToFirestore.TryGetValue(type, out converter)) return converter;
    if (type.IsPrimitive || type.IsEnum || type == typeof(string)) {
      _convertersToFirestore.Add(type, converter = value => value);
      return converter;
    }
    if (type.IsArray) {
      var elementType = type.GetElementType();
      if (elementType.IsPrimitive || elementType.IsEnum || elementType == typeof(string)) {
        _convertersToFirestore.Add(type, converter = value => value);
        return converter;
      }
      var elementConverter = GetConverterToFirestore(elementType);
      _convertersToFirestore.Add(type, converter = value => {
        if (value == null) return null;
        var list = new List<object>();
        foreach (var element in (IEnumerable)value) list.Add(elementConverter(element));
        return list;
      });
      return converter;
    }
    var populatorList = new List<Populator>();
    foreach (var field in type.GetFields()) {
      var idAttributes = (Id[])field.GetCustomAttributes(typeof(Id), false);
      if (idAttributes.Length == 0) continue;
      var key = $"{field.Name}${idAttributes[0].value}";
      var fieldConverter = GetConverterToFirestore(field.FieldType);
      populatorList.Add((value, dictionary) => {
        dictionary.Add(key, fieldConverter(field.GetValue(value)));
      });
    }
    var populators = populatorList.ToArray();
    _convertersToFirestore.Add(type, converter = value => {
      if (value == null) return null;
      var dictionary = new Dictionary<string, object>();
      foreach (var populator in populators) populator(value, dictionary);
      return dictionary;
    });
    return converter;
  }

  /// <summary>
  /// Returns a converter to generate values of the specified type from Firestore.
  /// </summary>
  public static FirestoreConverter GetConverterFromFirestore (Type type) {
    FirestoreConverter converter;
    if (_convertersFromFirestore.TryGetValue(type, out converter)) return converter;
    if (type.IsPrimitive || type.IsEnum || type == typeof(string)) {
      _convertersFromFirestore.Add(type, converter = value => Convert.ChangeType(value, type));
      return converter;
    }
    if (type.IsArray) {
      var elementType = type.GetElementType();
      var elementConverter = GetConverterFromFirestore(elementType);
      _convertersFromFirestore.Add(type, converter = value => {
        if (value == null) return null;
        var list = (List<object>)value;
        var array = Array.CreateInstance(elementType, list.Count);
        for (var ii = 0; ii < array.Length; ii++) array.SetValue(elementConverter(list[ii]), ii);
        return array;
      });
      return converter;
    }
    var populatorList = new List<Populator>();
    foreach (var field in type.GetFields()) {
      var idAttributes = (Id[])field.GetCustomAttributes(typeof(Id), false);
      if (idAttributes.Length == 0) continue;
      var key = $"{field.Name}${idAttributes[0].value}";
      var fieldConverter = GetConverterFromFirestore(field.FieldType);
      populatorList.Add((converted, dictionary) => {
        object value;
        if (dictionary.TryGetValue(key, out value)) {
          field.SetValue(converted, fieldConverter(value));
        }
      });
    }
    var populators = populatorList.ToArray();
    _convertersFromFirestore.Add(type, converter = value => {
      if (value == null) return null;
      var dictionary = (Dictionary<string, object>)value;
      var converted = Activator.CreateInstance(type);
      foreach (var populator in populators) populator(converted, dictionary);
      return converted;
    });
    return converter;
  }

  /// <summary>
  /// A set containing all of the ValueType generic types.
  /// </summary>
  public static readonly HashSet<Type> tupleTypes = new HashSet<Type> {
    typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>),
    typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>),
    typeof(ValueTuple<,,,,,,,>),
  };

  /// <summary>
  /// A type filter used to determine if the generic type definition matches the criterion provided.
  /// </summary>
  public static bool MatchesGenericType (Type type, object filterCriteria) {
    return type.IsGenericType && type.GetGenericTypeDefinition() == (Type)filterCriteria;
  }

  private static object Vector3ToFirestore (Vector3 value) {
    return new [] { value.x, value.y, value.z };
  }

  private static Vector3 Vector3FromFirestore (object value) {
    var list = value as IList;
    if (list == null) {
      Debug.LogWarning($"Invalid Vector3 value [value={value}].");
      return Vector3.zero;
    }
    return new Vector3((float)(double)list[0], (float)(double)list[1], (float)(double)list[2]);
  }

  private delegate void Populator (object value, Dictionary<string, object> dictionary);

  private static Dictionary<Type, FirestoreConverter> _convertersToFirestore =
      new Dictionary<Type, FirestoreConverter>() {
    { typeof(Vector3), value => Vector3ToFirestore((Vector3)value) },
  };

  private static Dictionary<Type, FirestoreConverter> _convertersFromFirestore =
      new Dictionary<Type, FirestoreConverter>() {
    { typeof(Vector3), value => Vector3FromFirestore(value) },
  };
}

}
