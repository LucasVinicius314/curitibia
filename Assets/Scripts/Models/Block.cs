using UnityEngine;

#nullable enable

record Block
{
  public Block(Material material, string name)
  {
    this.material = material;
    this.name = name;
  }

  public Material material;
  public string name;
}
