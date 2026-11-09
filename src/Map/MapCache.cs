using System.Collections.Generic;
using Curitibia.World;
using Godot;

namespace Curitibia.Map;

/// <summary>
/// The explored map: one small picture per chunk, drawn from the chunk's voxels when it loads or is edited, and kept after the
/// chunk unloads so everywhere the player has been stays on the map. Pictures are redrawn a few per frame (a burst of
/// chunks arriving never stalls a frame), each tile owns one Image and one texture for good (updated in place, nothing is
/// created while playing), and the whole cache is dropped when a new world starts.
/// </summary>
public partial class MapCache : Node
{
  const int TilesPerFrame = 8;

  sealed class Tile
  {
    public Image Image = null!;
    public ImageTexture Texture = null!;
  }

  public static MapCache? Instance { get; private set; }

  readonly Dictionary<Vector2I, Tile> tiles = new();
  readonly HashSet<Vector2I> dirty = new();
  readonly List<Vector2I> batch = new();
  readonly byte[] pixels = new byte[MapTileBuilder.Bytes];

  Terrain? bound;
  int boundVersion = -1;

  public override void _EnterTree()
  {
    Instance = this;
  }

  public override void _ExitTree()
  {
    if (bound != null && IsInstanceValid(bound))
    {
      bound.ChunkChanged -= OnChunkChanged;
    }

    if (Instance == this)
    {
      Instance = null;
    }
  }

  /// <summary>The picture of a chunk, or null when the player has never been near it.</summary>
  public Texture2D? TextureOf(Vector2I chunk)
  {
    return tiles.TryGetValue(chunk, out var tile) ? tile.Texture : null;
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (terrain != bound || (terrain != null && terrain.WorldVersion != boundVersion))
    {
      Rebind(terrain);
    }

    if (bound == null || dirty.Count == 0)
    {
      return;
    }

    batch.Clear();

    foreach (var chunk in dirty)
    {
      batch.Add(chunk);

      if (batch.Count >= TilesPerFrame)
      {
        break;
      }
    }

    foreach (var chunk in batch)
    {
      dirty.Remove(chunk);
      Redraw(chunk);
    }
  }

  void Rebind(Terrain? terrain)
  {
    if (bound != null && IsInstanceValid(bound))
    {
      bound.ChunkChanged -= OnChunkChanged;
    }

    tiles.Clear();
    dirty.Clear();
    bound = terrain;
    boundVersion = terrain?.WorldVersion ?? -1;

    if (terrain != null)
    {
      terrain.ChunkChanged += OnChunkChanged;
    }
  }

  void OnChunkChanged(Vector2I chunk)
  {
    dirty.Add(chunk);

    // The shading of a chunk's west and north edges looks at its neighbours, so the chunks east and south of this one change too.
    dirty.Add(chunk + Vector2I.Right);
    dirty.Add(chunk + Vector2I.Down);
  }

  void Redraw(Vector2I chunk)
  {
    if (bound == null || !bound.TryGetChunk(chunk, out var blocks) || blocks == null)
    {
      return; // not loaded (a neighbour that was only marked, or it has unloaded since): keep the picture it has
    }

    bound.TryGetChunk(chunk + Vector2I.Left, out var west);
    bound.TryGetChunk(chunk + Vector2I.Up, out var north);

    MapTileBuilder.Render(blocks, west, north, pixels);

    if (tiles.TryGetValue(chunk, out var tile))
    {
      tile.Image.SetData(MapTileBuilder.Size, MapTileBuilder.Size, false, Image.Format.Rgba8, pixels);
      tile.Texture.Update(tile.Image);

      return;
    }

    var image = Image.CreateFromData(MapTileBuilder.Size, MapTileBuilder.Size, false, Image.Format.Rgba8, pixels);

    tiles[chunk] = new Tile { Image = image, Texture = ImageTexture.CreateFromImage(image) };
  }
}
