using UnityEngine;

#nullable enable

static class Textures
{
  public static readonly Texture2D grass = (Texture2D)Utils.LoadPrefabFromFile("Textures/grass");
  public static readonly Texture2D stone = (Texture2D)Utils.LoadPrefabFromFile("Textures/stone");
  public static readonly Texture2D atlas = BuildAtlas();

  static Texture2D BuildAtlas()
  {
    const int tileWidth = 8;
    const int tileHeight = 8;

    var textures = new Texture2D[] {
      grass,
      stone,
    };

    var newAtlas = new Texture2D(tileWidth * textures.GetLength(0), tileHeight);

    newAtlas.wrapMode = TextureWrapMode.Repeat;
    newAtlas.filterMode = FilterMode.Point;

    for (int i = 0; i < textures.GetLength(0); i++)
    {
      var texture = textures[i];

      for (int x = 0; x < texture.width; x++)
      {
        var xOffset = i * tileWidth + x;

        for (int y = 0; y < texture.height; y++)
        {
          newAtlas.SetPixel(xOffset, y, texture.GetPixel(x, y));
        }
      }
    }

    newAtlas.Apply();

    return newAtlas;
  }
}
