namespace GGFolks.Server {

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WebSocketSharp.Server;

using Data;
using Protocol;

using GGFolks.React;

/// <summary>
/// The web socket server used both for editor testing and standalone (headless) operation.
/// </summary>
public static class Server<TRoot> where TRoot : AbstractRootObject, new() {

  /// <summary>
  /// The top-level dobject.
  /// </summary>
  public static readonly TRoot rootObject = new TRoot();

  /// <summary>
  /// The synchronization context of the thread that started the server (the main Unity thread).
  /// </summary>
  public static SynchronizationContext synchronizationContext { get; private set; }

  /// <summary>
  /// Returns the port upon which the server listens for connections: either the value of the
  /// HTTP_PORT environment variable or, by default, 8080.
  /// </summary>
  public static string port {
    get => Environment.GetEnvironmentVariable("HTTP_PORT") ?? "8080";
  }

  /// <summary>
  /// Starts listening for connections on the server.
  /// </summary>
  public static void Start () {
    rootObject.ServerInit(Path.root);
    rootObject.metaq.posted += OnMetaQueuePost;

    synchronizationContext = SynchronizationContext.Current;

    var httpServer = new HttpServer(Int32.Parse(port));
    // return OK for non-WebSocket requests to appease the health check
    httpServer.OnGet += (_, args) => args.Response.Close();
    httpServer.AddWebSocketService<Session<TRoot>>("/data");
    httpServer.Start();
    Debug.Log($"Listening for connections on port {port}.");
    Application.quitting += () => {
      httpServer.Stop();
      Debug.Log("Stopped listening for connections.");
    };
  }

  private static async void OnMetaQueuePost (object source, (MetaRequest, ISubscriber) args) {
    var (request, subscriber) = args;
    var session = (Session<TRoot>)subscriber;
    if (request is MetaRequest.Authenticate) {
      // TODO: validate token
      var authenticate = (MetaRequest.Authenticate)request;
      ((Mutable<string>)session.userId).Update(authenticate.userId);
      Debug.Log($"Client authenticated [who={session}].");

    } else if (request is MetaRequest.Subscribe) {
      var subscribe = (MetaRequest.Subscribe)request;
      try {
        var obj = await rootObject.Resolve(session, subscribe.path, 0);
        session.SubscribeToObject(subscribe.id, obj);

      } catch (FriendlyException e) {
        rootObject.metaq.Send(
          new MetaResponse.SubscribeFailed() { id = subscribe.id, cause = e.Message }, session);
      }
    } else if (request is MetaRequest.Unsubscribe) {
      var unsubscribe = (MetaRequest.Unsubscribe)request;
      session.UnsubscribeFromObject(unsubscribe.id);

    } else Debug.LogWarning($"Unknown meta-request type [who={session}, request={request}].");
  }
}

}
