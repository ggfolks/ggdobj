#if UNITY_EDITOR

namespace GGFolks {

using UnityEditor;

/// <summary>
/// Editor menu items for DObj bits.
/// </summary>
[InitializeOnLoad]
public static class DObjMenuItems {

  /// <summary>
  /// The name of the menu item and preference indicating that we should connect to the remote
  /// server.
  /// </summary>
  public const string ConnectToRemoteServerName = "GGFolks/Connect to Remote Server";

  static DObjMenuItems () {
    EditorApplication.update += OnFirstUpdate;
  }

  private static void OnFirstUpdate () {
    Menu.SetChecked(ConnectToRemoteServerName, EditorPrefs.GetBool(ConnectToRemoteServerName));
    EditorApplication.update -= OnFirstUpdate;
  }

  [MenuItem(ConnectToRemoteServerName)]
  private static void ToggleConnectToRemoteServer () {
    var connect = !Menu.GetChecked(ConnectToRemoteServerName);
    Menu.SetChecked(ConnectToRemoteServerName, connect);
    EditorPrefs.SetBool(ConnectToRemoteServerName, connect);
  }
}

}

#endif
