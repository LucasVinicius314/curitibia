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
  public const byte CopperOre = 15;
  public const byte TinOre = 16;
  public const byte IronOre = 17;
  public const byte SilverOre = 18;
  public const byte GoldOre = 19;
  public const byte PlatinumOre = 20;
  public const byte TallGrass = 21;

  /// <summary>The highest block id in use.</summary>
  public const byte MaxId = 21;

  /// <summary>Number of tiles in the atlas.</summary>
  public const int TileCount = 21; // the last tile is the void (window caps), the one before it the short grass blade picture (drawn as crossed quads, not as a cube); the six ore tiles come before those

  /// <summary>Atlas tile of the short grass block.</summary>
  public const int TuftTile = TileCount - 2;

  /// <summary>Atlas tile per block id (index = block id, -1 = not drawn).</summary>
  public static readonly int[] TileByBlock = { -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, TuftTile, 13, 14, 15, 16, 17, 18, TuftTile };

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
      case CopperOre: return "Copper ore";
      case TinOre: return "Tin ore";
      case IronOre: return "Iron ore";
      case SilverOre: return "Silver ore";
      case GoldOre: return "Gold ore";
      case PlatinumOre: return "Platinum ore";
      case TallGrass: return "Tall grass";
      default: return "Air";
    }
  }

  /// <summary>Hit points of a block: the damage its tool has to deal, hit after hit, to break it (a copper pickaxe deals 10 per hit: dirt takes 2 hits, stone 4, copper ore 5).</summary>
  public static float Durability(byte id)
  {
    return id switch
    {
      ShortGrass or TallGrass => 1f,
      Leaves or PineLeaves or BirchLeaves => 5f,
      Dirt or Sand or Snow or Grass or Cactus => 20f,
      Wood or BirchWood or Stone => 40f,
      CopperOre or TinOre => 50f,
      Hellrock or IronOre => 70f,
      SilverOre => 80f,
      GoldOre => 100f,
      PlatinumOre => 130f,
      Magma => 1000f,
      _ => 20f,
    };
  }

  /// <summary>The pickaxe power needed to break a block (0 = anything, a bare hand included). Magma needs more than any tool.</summary>
  public static int RequiredPower(byte id)
  {
    return id switch
    {
      Stone or CopperOre or TinOre => 1,
      IronOre or SilverOre => 2,
      GoldOre or Hellrock => 3,
      PlatinumOre => 4,
      Magma => 99,
      _ => 0,
    };
  }

  /// <summary>The block a broken block drops as an item (0 = nothing): grass gives dirt, short grass and magma give nothing.</summary>
  public static byte Drop(byte id)
  {
    return id switch
    {
      Air or ShortGrass or TallGrass or Magma => Air,
      Grass => Dirt,
      _ => id,
    };
  }

  /// <summary>A block you walk through and that is drawn as crossed quads (short and tall grass): no collision, hides no face of its neighbours, but it can still be targeted and broken.</summary>
  public static bool IsPlant(byte id)
  {
    return id is ShortGrass or TallGrass;
  }

  /// <summary>Whether the block stops bodies and hides the faces behind it.</summary>
  public static bool IsSolid(byte id)
  {
    return id != Air && !IsPlant(id);
  }
}
