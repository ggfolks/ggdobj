namespace GGFolks.Protocol {

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Represents a collection property in a distributed object.
/// </summary>
public class DCollection<T> : DProperty where T : DObject, new() {

  /// <summary>
  /// Type for delegates that check asynchronously whether a subscriber can access a member.
  /// </summary>
  public delegate Task<bool> CanAccess (ISubscriber subscriber, string key);

  /// <summary>
  /// Type for delegates that asynchronously populate newly created members (e.g., from a database).
  /// </summary>
  public delegate Task Populate (T obj);

  /// <summary>
  /// If non-null, used to check whether sessions can access individual collection members.
  /// </summary>
  public CanAccess canAccess;

  /// <summary>
  /// If non-null, used to populate newly created collection members.
  /// </summary>
  public Populate populate;

  /// <summary>
  /// Resolves the collection member identified by the given key.
  /// </summary>
  public T Resolve (string key) {
    return _owner.client.Resolve<T>(_owner.path.Concat((_id, key)), _backing, canAccess, populate);
  }

  public override void Init (DObject owner, string name, uint id, object ctx, BackingType backing) {
    base.Init(owner, name, id, ctx, backing);
    _backing = backing;
  }

  public override async Task<DObject> Resolve (ISession session, Path path, int index) {
    var (_, key) = path.elements[index];
    if (canAccess != null) {
      var accessible = await canAccess(session, key);
      if (!accessible) {
        Debug.LogWarning($"Denied access to object [who={session}, path={path}, index={index}].");
        throw new FriendlyException("Access denied.");
      }
    }
    Task<T> task;
    if (!_resolved.TryGetValue(key, out task)) _resolved.Add(key, task = CreateObject(key));
    var obj = await task;
    if (index == path.elements.Length - 1) return obj;
    return await obj.Resolve(session, path, index + 1);
  }

  private async Task<T> CreateObject (string key) {
    var obj = new T();
    obj.ServerInit(_owner.path.Concat((_id, key)));
    if (populate != null) await populate(obj);
    return obj;
  }

  private Dictionary<string, Task<T>> _resolved = new Dictionary<string, Task<T>>();
  private BackingType _backing;
}

}
