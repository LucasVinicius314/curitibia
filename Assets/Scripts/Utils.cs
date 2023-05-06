using UnityEngine;
using System.IO;
#nullable enable
class Utils
{
    public static GameObject LoadPrefabFromFile(string filename)
    {
        Debug.Log("Trying to load LevelPrefab from file (" + filename + ")...");
        var loadedObject = Resources.Load(filename);
        if (loadedObject == null)
        {
            throw new FileNotFoundException("...no file found - please check the configuration");
        }
        return (GameObject)loadedObject;
    }
}
