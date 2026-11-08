using Godot;

namespace Curitibia.World;

/// <summary>
/// One row of 8x8 block tiles built once from the block textures, plus the shared chunk material (nearest filtering, no
/// mipmaps, so the pixel art stays crisp). Built once, never per frame.
/// </summary>
public static class TextureAtlas
{
  public const int TilePixels = 8;

  static readonly string[] tilePaths =
  {
    "res://assets/textures/grass.png", // tile 0 (BlockIds.Grass)
    "res://assets/textures/stone.png", // tile 1 (BlockIds.Stone)
    "res://assets/textures/wood.png",   // tile 2 (BlockIds.Wood)
    "res://assets/textures/leaves.png", // tile 3 (BlockIds.Leaves)
  };

  static StandardMaterial3D? material;

  /// <summary>UV inset per tile edge, as a fraction of the whole atlas width: a quarter texel.</summary>
  public static float UvInset => 0.25f / (TilePixels * BlockIds.TileCount);

  public static Material ChunkMaterial => material ??= Build();

  static StandardMaterial3D Build()
  {
    var atlas = Image.CreateEmpty(TilePixels * tilePaths.Length, TilePixels, false, Image.Format.Rgba8);

    for (int i = 0; i < tilePaths.Length; i++)
    {
      var image = GD.Load<Texture2D>(tilePaths[i]).GetImage();

      if (image.IsCompressed())
      {
        image.Decompress();
      }

      image.Convert(Image.Format.Rgba8);
      image.Resize(TilePixels, TilePixels, Image.Interpolation.Nearest);
      atlas.BlitRect(image, new Rect2I(0, 0, TilePixels, TilePixels), new Vector2I(i * TilePixels, 0));
    }

    return new StandardMaterial3D
    {
      AlbedoTexture = ImageTexture.CreateFromImage(atlas),
      TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
      Roughness = 1f,
    };
  }
}
