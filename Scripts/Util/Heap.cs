namespace GGFolks.Util {

using System;
using System.Collections.Generic;

/// <summary>
/// A simple list-backed heap.
/// </summary>
public class Heap<T> where T : IComparable<T> {

  /// <summary>
  /// The number of elements in the heap.
  /// </summary>
  public int count { get => _list.Count; }

  /// <summary>
  /// Adds an element to the heap.
  /// </summary>
  public void Add (T element) {
    _list.Add(element);

    // https://en.wikipedia.org/wiki/Binary_heap#Insert
    for (var idx = _list.Count - 1; idx > 0; ) {
      var parentIdx = (idx - 1) >> 1;
      var parent = _list[parentIdx];
      if (element.CompareTo(parent) >= 0) break;
      _list[idx] = parent;
      _list[parentIdx] = element;
      idx = parentIdx;
    }
  }

  /// <summary>
  /// Removes the lowest element from the heap and returns it.
  /// </summary>
  public T TakeLowest () {
    var lowest = _list[0];

    var lastIndex = _list.Count - 1;
    var lastElement = _list[lastIndex];
    _list.RemoveAt(lastIndex);
    if (_list.Count == 0) return lowest;
    
    // filter down the heap
    // https://en.wikipedia.org/wiki/Binary_heap#Extract
    _list[0] = lastElement;
    for (var idx = 0;; ) {
      var leftIdx = (idx << 1) + 1;
      var rightIdx = leftIdx + 1;
      var smallestIdx = idx;
      if (leftIdx < _list.Count) {
        if (_list[leftIdx].CompareTo(lastElement) < 0) smallestIdx = leftIdx;
        if (rightIdx < _list.Count && _list[rightIdx].CompareTo(_list[smallestIdx]) < 0) {
          smallestIdx = rightIdx;
        }
      }
      if (smallestIdx == idx) break;
      _list[idx] = _list[smallestIdx];
      _list[smallestIdx] = lastElement;
      idx = smallestIdx;
    }

    return lowest;
  }

  private List<T> _list = new List<T>();
}

}
