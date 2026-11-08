using System;
using System.IO;
using System.Linq;
using P2PSignaling;

namespace Curitibia.Net;

/// <summary>Adds friends to the library's shared friends file (one id per line, a # note after it).</summary>
public static class FriendsFile
{
  public static string FilePath => Path.Combine(P2PIdentity.HomeFolder, "friends");

  public static bool TryAdd(string rawId, string label, out string message)
  {
    if (!P2PIdentity.TryParseId(rawId, out var id))
    {
      message = "That is not a valid friend ID (check for typos).";
      return false;
    }

    if (P2PIdentity.Format(id) == P2PIdentity.OwnId)
    {
      message = "That is your own ID.";
      return false;
    }

    var list = P2PIdentity.LoadFriends(); // also creates the file, with its explanation, when it is missing

    if (list.Friends.Any(f => f.Id == P2PIdentity.Format(id)))
    {
      message = "Already in your friends list.";
      return false;
    }

    try
    {
      var note = label.Trim().Replace('\n', ' ').Replace('\r', ' ');
      var line = P2PIdentity.Format(id) + (note.Length > 0 ? "   # " + note : "");
      var existing = File.ReadAllText(FilePath);
      var separator = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";

      File.AppendAllText(FilePath, separator + line + "\n");
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
    {
      message = $"Could not write {FilePath} ({e.Message}).";
      return false;
    }

    message = "Friend added.";

    return true;
  }
}
