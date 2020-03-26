namespace GGFolks.React {

using System;
using System.Collections.Generic;

/// <summary>
/// A simple reactive value.
/// </summary>
public class Value<T> {

  /// <summary>
  /// Retrieves or sets the current value.
  /// </summary>
  public T current {
    get => _current;
    set {
      if (Object.Equals(_current, value)) return;
      _current = value;
      for (var ii = _listeners.Count - 1; ii >= 0; ii--) _listeners[ii](_current);
    }
  }

  /// <summary>
  /// Adds a listener that will be called for the current value and upon any changes.
  /// </summary>
  /// <returns>An action to use to remove the listener.</returns>
  public Action OnValue (Action<T> listener) {
    listener(_current);
    _listeners.Add(listener);
    return () => _listeners.Remove(listener);
  }

  private T _current;
  private List<Action<T>> _listeners = new List<Action<T>>();
}

}
