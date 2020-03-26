namespace GGFolks.Util {

using System;
using System.Collections.Generic;

/// <summary>
/// Maintains a list of actions to take on disposal.
/// </summary>
public class Disposer : IDisposable {

  /// <summary>
  /// Adds an action to perform on disposal.
  /// </summary>
  public void Add (Action action) {
    _actions.Add(action);
  }

  // defined by IDisposable
  public void Dispose () {
    foreach (var action in _actions) action();
    _actions.Clear();
  }

  private List<Action> _actions = new List<Action>();
}

}
