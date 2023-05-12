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
}
