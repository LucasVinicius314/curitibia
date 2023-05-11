using UnityEngine;
using System.IO;

#nullable enable

class Utils
{
  public static Object LoadPrefabFromFile(string fileName)
  {
    Debug.Log($"Loading {fileName}.");

    var loadedObject = Resources.Load(fileName);

    if (loadedObject == null)
    {
      throw new FileNotFoundException($"{fileName} not found.");
    }

    return loadedObject;
  }

  public static void LerpTransform(Transform t1, Transform t2, float t)
  {
    t1.position = Vector3.Lerp(t1.position, t2.position, t);
    t1.rotation = Quaternion.Lerp(t1.rotation, t2.rotation, t);
    t1.localScale = Vector3.Lerp(t1.localScale, t2.localScale, t);
  }
}
