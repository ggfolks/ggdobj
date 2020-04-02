namespace GGFolks.Util {

using System;
using System.Collections.Generic;

using React;

/// <summary>
/// Maintains a list of actions to take on disposal.
/// </summary>
public class Disposer : IDisposable {

  /// <summary>
  /// Adds a Remover to run on disposal.
  /// </summary>
  public void Add (Remover remover) {
    _removers.Add(remover);
  }

  /// <summary>
  /// Adds an action to perform on disposal.
  /// </summary>
  public void AddAction (Action action) {
    _removers.Add(() => action());
  }

  // defined by IDisposable
  public void Dispose () {
    foreach (var remover in _removers) remover();
    _removers.Clear();
  }

  private List<Remover> _removers = new List<Remover>();
}

}
