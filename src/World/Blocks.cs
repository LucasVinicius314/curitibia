namespace Curitibia.World;

/// <summary>Block ids stored in chunk arrays. 0 is air. Engine-free so the generator and mesher can be unit-tested.</summary>
public static class BlockIds
{
  public const byte Air = 0;
  public const byte Grass = 1;
  public const byte Stone = 2;
  public const byte Wood = 3;
  public const byte Leaves = 4;

  /// <summary>Number of tiles in the atlas.</summary>
  public const int TileCount = 4;

  /// <summary>Atlas tile per block id (index = block id, -1 = not drawn).</summary>
  public static readonly int[] TileByBlock = { -1, 0, 1, 2, 3 };

  /// <summary>Blocks the player can place, in hotbar order.</summary>
  public static readonly byte[] Placeable = { Stone, Grass, Wood, Leaves };

  public static string DisplayName(byte id)
  {
    switch (id)
    {
      case Grass: return "Grass";
      case Stone: return "Stone";
      case Wood: return "Wood";
      case Leaves: return "Leaves";
      default: return "Air";
    }
  }

  public static bool IsSolid(byte id)
  {
    return id != Air;
  }
}
