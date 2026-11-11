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
    "res://assets/textures/dirt.png",   // tile 4 (BlockIds.Dirt)
    "res://assets/textures/sand.png",   // tile 5 (BlockIds.Sand)
    "res://assets/textures/snow.png",   // tile 6 (BlockIds.Snow)
    "res://assets/textures/cactus.png", // tile 7 (BlockIds.Cactus)
    "res://assets/textures/pine_leaves.png", // tile 8 (BlockIds.PineLeaves)
    "res://assets/textures/birch_log.png", // tile 9 (BlockIds.BirchWood)
    "res://assets/textures/birch_leaves.png", // tile 10 (BlockIds.BirchLeaves)
    "res://assets/textures/hellrock.png", // tile 11 (BlockIds.Hellrock)
    "res://assets/textures/magma.png", // tile 12 (BlockIds.Magma)
    "res://assets/textures/copper_ore.png", // tile 13 (BlockIds.CopperOre)
    "res://assets/textures/tin_ore.png", // tile 14
    "res://assets/textures/iron_ore.png", // tile 15
    "res://assets/textures/silver_ore.png", // tile 16
    "res://assets/textures/gold_ore.png", // tile 17
    "res://assets/textures/platinum_ore.png", // tile 18 (BlockIds.PlatinumOre)
    "res://assets/textures/tall_grass.png", // tile 19: the grass tuft (BlockIds.TuftTile), transparent around the blades
    "res://assets/textures/void.png", // tile 20: the void, for the faces that close the meshing window (the last tile)
  };

  static ShaderMaterial? material;

  /// <summary>UV inset per tile edge, as a fraction of the whole atlas width: a quarter texel.</summary>
  public static float UvInset => 0.25f / (TilePixels * BlockIds.TileCount);

  public static Material ChunkMaterial => material ??= Build();

  static ShaderMaterial Build()
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

    var shader = new Shader
    {
      // One tile of the atlas per quad, repeated across merged quads: the vertex colour's alpha is the tile index, its rgb the biome
      // tint, the UV counts tiles. Nearest filtering and no mipmaps keep the pixel art crisp; the tufts of grass are cut out.
      Code = @"shader_type spatial;
render_mode cull_back, depth_draw_opaque;

uniform sampler2D atlas : source_color, filter_nearest, repeat_disable;
uniform float tile_count = 21.0;

varying flat float tile;

void vertex() {
  tile = floor(COLOR.a * 255.0 + 0.5);
}

void fragment() {
  vec2 inside = fract(UV);
  vec4 texel = texture(atlas, vec2((tile + inside.x) / tile_count, inside.y));
  ALBEDO = texel.rgb * COLOR.rgb;
  ROUGHNESS = 1.0;
  ALPHA = texel.a;
  ALPHA_SCISSOR_THRESHOLD = 0.5;
}
",
    };

    var material = new ShaderMaterial { Shader = shader };

    material.SetShaderParameter("atlas", ImageTexture.CreateFromImage(atlas));
    material.SetShaderParameter("tile_count", (float)tilePaths.Length);

    return material;
  }
}
