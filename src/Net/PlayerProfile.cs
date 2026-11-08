using System;
using P2PSignaling;

namespace Curitibia.Net;

/// <summary>
/// The player's display name, kept by the p2p library in ~/.p2p-signaling-server/name so every game on this machine shows the
/// same one: the player picks it once. Until one is chosen it falls back to the OS user name.
/// </summary>
public static class PlayerProfile
{
  public static string Name
  {
    get
    {
      var shared = P2PIdentity.PlayerName();

      if (shared != null)
      {
        return shared;
      }

      var fallback = P2PIdentity.CleanPlayerName(Environment.UserName);

      return fallback.Length > 0 ? fallback : "Player";
    }
  }

  /// <summary>The player typed a name: save it for every game (empty names are ignored).</summary>
  public static void Save(string name)
  {
    P2PIdentity.SavePlayerName(name);
  }
}
