namespace Curitibia.Core;

public enum Team
{
  Neutral,
  Players,
  Enemies,
}

public static class TeamExtensions
{
  public static bool IsHostileTo(this Team self, Team other)
  {
    return self != Team.Neutral && other != Team.Neutral && self != other;
  }
}
