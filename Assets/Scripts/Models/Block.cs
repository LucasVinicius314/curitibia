using UnityEngine;

#nullable enable

public record Block
{
  public Block(Texture2D texture, string name)
  {
    this.texture = texture;
    this.name = name;
  }

  public Texture2D texture;
  public string name;

  public static Block stone = new Block(name: "Stone", texture: Textures.stone);
  public static Block grass = new Block(name: "Grass", texture: Textures.grass);
}
