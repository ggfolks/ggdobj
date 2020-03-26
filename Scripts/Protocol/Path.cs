namespace GGFolks.Protocol {

using System;

/// <summary>
/// Represents the path of a distributed object.
/// </summary>
public readonly struct Path {

  /// <summary>
  /// The path of the root object.
  /// </summary>
  public static readonly Path root = new Path(new (uint, string)[0]);

  /// <summary>
  /// The individual path elements.
  /// </summary>
  [Id(1)] public readonly (uint id, string key)[] elements;

  /// <summary>
  /// Creates a path with the specified elements.
  /// </summary>
  public Path ((uint, string)[] elements) {
    this.elements = elements;
  }

  /// <summary>
  /// Gets the child of this path formed by appending the specified element.
  /// </summary>
  public Path Concat ((uint, string) element) {
    var newElements = new (uint, string)[elements.Length + 1];
    Array.Copy(elements, newElements, elements.Length);
    newElements[elements.Length] = element;
    return new Path(newElements);
  }

  public override bool Equals (object obj) {
    if (!(obj is Path)) return false;
    var other = (Path)obj;
    if (elements.Length != other.elements.Length) return false;
    for (var ii = 0; ii < elements.Length; ii++) {
      if (elements[ii] != other.elements[ii]) return false;
    }
    return true;
  }

  public override int GetHashCode () {
    int code = 0;
    foreach (var element in elements) code ^= element.GetHashCode();
    return code;
  }

  public override string ToString () {
    return $"[{String.Join("/", elements)}]";
  }
}

}
