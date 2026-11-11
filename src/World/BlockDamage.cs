using System;
using System.Collections.Generic;

namespace Curitibia.World;

/// <summary>What one hit did to a block.</summary>
public enum MiningResult
{
  /// <summary>Nothing to mine (air).</summary>
  Idle,

  /// <summary>The block took damage and is still standing.</summary>
  Mining,

  /// <summary>The tool is too weak for this block: no damage.</summary>
  TooHard,

  /// <summary>The block ran out of durability: break it.</summary>
  Broken,
}

/// <summary>
/// The damage the blocks of the world have taken, engine-free: it belongs to the block, not to who hit it, so every player's
/// swings add up. A hit deals the tool's damage; a block has <see cref="BlockIds.Durability"/> to lose and
/// <see cref="BlockIds.RequiredPower"/> the tool must have. Damage stays <see cref="Lifetime"/> seconds after the last hit on
/// the block and then heals completely.
/// </summary>
public sealed class BlockDamage
{
  /// <summary>Seconds a damaged block keeps its cracks after the last hit.</summary>
  public const float Lifetime = 10f;

  struct Entry
  {
    public byte Block;
    public float Damage;
    public float Age;
  }

  readonly Dictionary<(int, int, int), Entry> map = new();

  /// <summary>Blocks that are cracked right now.</summary>
  public int Count => map.Count;

  /// <summary>One hit on the block at (x, y, z) from a tool of the given power that deals <paramref name="damage"/>.</summary>
  public MiningResult Hit(int x, int y, int z, byte block, int power, float damage)
  {
    var key = (x, y, z);

    if (block == BlockIds.Air)
    {
      map.Remove(key);

      return MiningResult.Idle;
    }

    if (power < BlockIds.RequiredPower(block))
    {
      return MiningResult.TooHard;
    }

    map.TryGetValue(key, out var entry);

    if (entry.Block != block)
    {
      entry = new Entry { Block = block }; // a different block than the damage was dealt to (it was replaced): start over
    }

    entry.Damage += Math.Max(0f, damage);
    entry.Age = 0f;

    if (entry.Damage >= BlockIds.Durability(block))
    {
      map.Remove(key);

      return MiningResult.Broken;
    }

    map[key] = entry;

    return MiningResult.Mining;
  }

  /// <summary>0 to 1: how much of the block's durability is gone.</summary>
  public float Progress(int x, int y, int z)
  {
    return map.TryGetValue((x, y, z), out var entry) ? Math.Clamp(entry.Damage / BlockIds.Durability(entry.Block), 0f, 1f) : 0f;
  }

  /// <summary>The crack stage to draw, 0 (none) to <paramref name="stages"/>: grows as the block takes damage.</summary>
  public int Stage(int x, int y, int z, int stages)
  {
    var progress = Progress(x, y, z);

    return progress <= 0f ? 0 : Math.Clamp((int)MathF.Ceiling(progress * stages), 1, stages);
  }

  /// <summary>Forgets the damage of a block (it was broken or replaced); true when there was any.</summary>
  public bool Clear(int x, int y, int z)
  {
    return map.Remove((x, y, z));
  }

  /// <summary>Ages every damaged block; the ones that went <see cref="Lifetime"/> seconds without a hit heal and are added to <paramref name="healed"/>.</summary>
  public void Tick(float dt, List<(int X, int Y, int Z)> healed)
  {
    if (map.Count == 0)
    {
      return;
    }

    List<(int, int, int)>? expired = null;

    foreach (var (key, entry) in map)
    {
      if (entry.Age + dt >= Lifetime)
      {
        (expired ??= new List<(int, int, int)>()).Add(key);
      }
    }

    // Entries are structs in a dictionary: age them by rewriting (the keys are copied first, so the map is not changed while walking it).
    var keys = new List<(int, int, int)>(map.Keys);

    foreach (var key in keys)
    {
      var entry = map[key];

      entry.Age += dt;
      map[key] = entry;
    }

    if (expired == null)
    {
      return;
    }

    foreach (var key in expired)
    {
      map.Remove(key);
      healed.Add(key);
    }
  }
}
