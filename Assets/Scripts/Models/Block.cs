using UnityEngine;

#nullable enable

record Block
{
  public Block(Texture2D texture, string name)
  {
    this.texture = texture;
    this.name = name;
  }

  public Texture2D texture;
  public string name;
}
