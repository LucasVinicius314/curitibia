using System;
using System.IO;
using P2PSignaling;

namespace Curitibia.Net;

/// <summary>Where the game's signaling server address comes from. The game never contains an address of its own.</summary>
public enum ServerSource
{
  None,
  Environment,
  File,
}

/// <summary>
/// The p2p library resolves the server as: the game's own Url (we never set it), else the P2P_SIGNALING_URL environment
/// variable, else the first line of ~/.p2p-signaling-server/server (shared by every game that uses the library). This is the
/// game's view of that, so the settings page can show it and write the shared file.
/// </summary>
public static class ServerSettings
{
  const string EnvName = "P2P_SIGNALING_URL";

  public static string? Current => P2PIdentity.ServerUrl();

  public static string FilePath => Path.Combine(P2PIdentity.HomeFolder, "server");

  public static ServerSource Source
  {
    get
    {
      if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvName)))
      {
        return ServerSource.Environment;
      }

      return Current != null ? ServerSource.File : ServerSource.None;
    }
  }

  /// <summary>Points this process at a server without touching the shared file (dev flag --net-url).</summary>
  public static void OverrideForThisRun(string url)
  {
    Environment.SetEnvironmentVariable(EnvName, url.Trim());
  }

  /// <summary>Writes the shared server file (replacing it: this is the player's explicit choice).</summary>
  public static bool TrySave(string raw, out string message)
  {
    var url = raw.Trim();

    if (!TryNormalise(url, out var normalised, out message))
    {
      return false;
    }

    try
    {
      Directory.CreateDirectory(P2PIdentity.HomeFolder);
      File.WriteAllText(
        FilePath,
        "# The p2p-signaling server games on this machine use when they do not set one themselves\n" +
        "# (first line that is not a comment). Written by Curitibia's settings page; edit it to switch servers.\n" +
        normalised + "\n");
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
    {
      message = $"Could not write {FilePath} ({e.Message}).";
      return false;
    }

    message = Source == ServerSource.Environment
      ? $"Saved, but the {EnvName} environment variable still overrides it for this run."
      : "Saved.";

    return true;
  }

  static bool TryNormalise(string url, out string normalised, out string message)
  {
    normalised = url;
    message = "";

    if (url.Length == 0)
    {
      message = "Type the server address first, for example https://example.com";
      return false;
    }

    if (!url.Contains("://"))
    {
      normalised = "https://" + url;
    }

    if (!Uri.TryCreate(normalised, UriKind.Absolute, out var uri) || uri.Host.Length == 0
      || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "ws" && uri.Scheme != "wss"))
    {
      message = "That does not look like a server address (use http://, https://, ws:// or wss://).";
      return false;
    }

    normalised = normalised.TrimEnd('/');

    return true;
  }
}
