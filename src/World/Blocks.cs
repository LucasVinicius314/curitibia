namespace Curitibia.World;

/// <summary>Block ids stored in chunk arrays. 0 is air. Engine-free so the generator and mesher can be unit-tested.</summary>
public static class BlockIds
{
  public const byte Air = 0;
  public const byte Grass = 1;
  public const byte Stone = 2;
  public const byte Wood = 3;
  public const byte Leaves = 4;
  public const byte Dirt = 5;
  public const byte Sand = 6;
  public const byte Snow = 7;
  public const byte Cactus = 8;
  public const byte PineLeaves = 9;
  public const byte BirchWood = 10;
  public const byte BirchLeaves = 11;
  public const byte Hellrock = 12;
  public const byte Magma = 13;
  public const byte ShortGrass = 14;

  /// <summary>The highest block id in use.</summary>
  public const byte MaxId = 14;

  /// <summary>Number of tiles in the atlas.</summary>
  public const int TileCount = 15; // the last tile is the void (window caps), the one before it the short grass blade picture (drawn as crossed quads, not as a cube)

  /// <summary>Atlas tile of the short grass block.</summary>
  public const int TuftTile = TileCount - 2;

  /// <summary>Atlas tile per block id (index = block id, -1 = not drawn).</summary>
  public static readonly int[] TileByBlock = { -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, TuftTile };

  /// <summary>Blocks the player can place, in hotbar order.</summary>
  public static readonly byte[] Placeable = { Stone, Grass, Wood, Leaves };

  public static string DisplayName(byte id)
  {
    switch (id)
    {
      case Grass: return "Grass";
      case Stone: return "Stone";
      case Wood: return "Oak log";
      case Leaves: return "Oak leaves";
      case Dirt: return "Dirt";
      case Sand: return "Sand";
      case Snow: return "Snow";
      case Cactus: return "Cactus";
      case PineLeaves: return "Pine leaves";
      case BirchWood: return "Birch log";
      case BirchLeaves: return "Birch leaves";
      case Hellrock: return "Hellrock";
      case Magma: return "Magma";
      case ShortGrass: return "Short grass";
      default: return "Air";
    }
  }

  /// <summary>A block you walk through and that is drawn as crossed quads (short grass): no collision, hides no face of its neighbours, but it can still be targeted and broken.</summary>
  public static bool IsPlant(byte id)
  {
    return id == ShortGrass;
  }

  /// <summary>Whether the block stops bodies and hides the faces behind it.</summary>
  public static bool IsSolid(byte id)
  {
    return id != Air && !IsPlant(id);
  }
}
