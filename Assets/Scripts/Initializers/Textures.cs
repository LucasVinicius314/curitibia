using System;
using System.Collections.Generic;
using UnityEngine;

#nullable enable

static class Textures
{
  static readonly Dictionary<Texture2D, long> textureOffsetDict = new Dictionary<Texture2D, long>();

  public static readonly Texture2D grass = (Texture2D)Utils.LoadPrefabFromFile("Textures/grass");
  public static readonly Texture2D stone = (Texture2D)Utils.LoadPrefabFromFile("Textures/stone");
  public static readonly Texture2D atlas = BuildAtlas();


  public static (float, float) GetTextureOffset(Texture2D texture)
  {
    if (!textureOffsetDict.ContainsKey(texture))
    {
      throw new ArgumentException("Invalid texture offset.");
    }

    var offset = (float)textureOffsetDict[texture];

    return (offset / textureOffsetDict.Count, (offset + 1f) / textureOffsetDict.Count);
  }

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

      textureOffsetDict[texture] = i;

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
