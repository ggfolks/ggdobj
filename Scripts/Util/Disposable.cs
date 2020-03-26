namespace GGFolks.Util {

using System;

/// <summary>
/// Simple base class for objects that use a Disposer to manage actions to take on disposal.
/// See https://docs.microsoft.com/en-us/dotnet/api/system.idisposable?view=netframework-4.8
/// </summary>
public abstract class Disposable : IDisposable {

  /// <summary>
  /// The disposer containing the actions to run on disposal.
  /// </summary>
  public readonly Disposer disposer = new Disposer();

  // defined by IDisposable
  public void Dispose () {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  ~Disposable () {
    Dispose(false);
  }

  /// <summary>
  /// Called on disposal/finalization.
  /// </summary>
  protected virtual void Dispose (bool disposing) {
    disposer.Dispose();
  }
}

}
