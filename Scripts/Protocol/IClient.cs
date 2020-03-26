namespace GGFolks.Protocol {

using System.Threading.Tasks;
using Firebase.Firestore;

/// <summary>
/// Interface to be implemented by clients.
/// </summary>
public interface IClient : ISubscriber {

  /// <summary>
  /// Returns a task that will resolve to the Firestore instance.
  /// <summary>
  Task<FirebaseFirestore> firestore { get; }

  /// <summary>
  /// Resolves the object at the specified path with the given backing type.
  /// </summary>
  T Resolve<T> (
    Path path, BackingType backing, DCollection<T>.CanAccess canAccess,
    DCollection<T>.Populate populate) where T : DObject, new();

  /// <summary>
  /// Returns the Firestore path corresponding to the given object path.
  /// </summary>
  string GetFirestorePath (Path path);
}

}
