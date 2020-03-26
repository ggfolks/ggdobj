namespace GGFolks {

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Firebase;
using Firebase.Auth;
using Firebase.Firestore;

using Client;
using Data;
using React;
using Server;

/// <summary>
/// Base class for dobj apps.  The initial scene should contain a GameObject with this behavior.
/// </summary>
public abstract class AbstractApp<TRoot> : MonoBehaviour where TRoot : AbstractRootObject, new() {

  /// <summary>
  /// Returns the single App instance, which will be initialized if it has not been already.
  /// </summary>
  public static AbstractApp<TRoot> instance {
    get {
      if (_instance == null) throw new Exception("No App instance present.");
      _instance.MaybeInit();
      return _instance;
    }
  }

  [Tooltip("The WebSocket URL to which the client connects.")]
  public string webSocketURL = "wss://empowered.tfw.dev/data";

  /// <summary>
  /// The game client, used to access distributed objects.
  /// </summary>
  public Client<TRoot> client { get; private set; }

  /// <summary>
  /// A task that will resolve to the Firebase app when initialized.
  /// </summary>
  public Task<FirebaseApp> firebaseApp { get; private set; }

  /// <summary>
  /// A task that will resolve to the Firebase auth object when initialized.
  /// </summary>
  public Task<FirebaseAuth> firebaseAuth { get; private set; }

  /// <summary>
  /// A task that will resolve to the Cloud Firestore object when initialized.
  /// </summary>
  public Task<FirebaseFirestore> firestore { get; private set; }

  public AbstractApp () {
    if (_instance != null) Debug.LogWarning("Multiple App instances present.");
    _instance = this;
  }

  /// <summary>
  /// Called after parent class initialization to initialize the client.
  /// </summary>
  protected virtual void InitClient (TRoot rootObject) {
    // nothing by default
  }

  /// <summary>
  /// Called after parent class initialization to initialize the server.
  /// </summary>
  protected virtual void InitServer (TRoot rootObject) {
    // nothing by default
  }

  private void Awake () {
    MaybeInit();
  }

  private void MaybeInit () {
    if (_initialized) return;
    _initialized = true;

    // if we're running headless, all we want is the server
    if (Application.isBatchMode) {
      Server<TRoot>.Start();
      InitServer(Server<TRoot>.rootObject);
      return;
    }

    firebaseApp = InitFirebase();
    firebaseAuth = InitFirebaseAuth();
    firestore = InitFirestore();

    // if we're running in play mode in the editor, we may want the local server
    var webSocketURL = this.webSocketURL;
    #if UNITY_EDITOR
      if (!EditorPrefs.GetBool(DObjMenuItems.ConnectToRemoteServerName)) {
        webSocketURL = $"ws://localhost:{Server<TRoot>.port}/data";
        Server<TRoot>.Start();
        InitServer(Server<TRoot>.rootObject);
      }
    #endif

    client = new Client<TRoot>(webSocketURL);
    InitClient(client.rootObject);
  }

  private async Task<FirebaseApp> InitFirebase () {
    var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
    if (dependencyStatus != DependencyStatus.Available) {
      throw new Exception($"Could not resolve all Firebase dependencies: {dependencyStatus}.");
    }
    return FirebaseApp.DefaultInstance;
  }

  private async Task<FirebaseAuth> InitFirebaseAuth () {
    await this.firebaseApp;
    var firebaseAuth = FirebaseAuth.DefaultInstance;
    Action maybeSignIn = () => {
      if (firebaseAuth.CurrentUser == null) firebaseAuth.SignInAnonymouslyAsync();
    };
    firebaseAuth.StateChanged += (source, args) => maybeSignIn();
    maybeSignIn();
    return firebaseAuth;
  }

  private async Task<FirebaseFirestore> InitFirestore () {
    var firebaseApp = await this.firebaseApp;
    return FirebaseFirestore.DefaultInstance;
  }

  private void OnDestroy () {
    client?.Dispose();
  }

  private static AbstractApp<TRoot> _instance;

  private bool _initialized;
}

}
