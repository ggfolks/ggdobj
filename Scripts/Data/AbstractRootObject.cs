﻿namespace GGFolks.Data {

using Protocol;

/// <summary>
/// The base class for root DObjects.
/// </summary>
public abstract class AbstractRootObject : DObject {

  /// <summary>
  /// The queue for requests concerning objects.
  /// </summary>
  [Id(1)]
  public readonly DQueue<MetaRequest, MetaResponse> metaq;
}

}
