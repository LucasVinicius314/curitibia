using System;
using System.Collections.Generic;
using Godot;

namespace Curitibia.Items;

/// <summary>
/// Renders each item's model once into an icon texture (an offscreen viewport with a transparent background and an
/// orthographic camera), so icons always match the models and a new item needs no art. The textures are cached for the whole
/// run; this node only does the rendering and frees its viewport afterwards. Nothing here runs per frame.
/// </summary>
public partial class ItemIconRenderer : Node
{
  const int IconPixels = 128;

  static readonly Dictionary<int, Texture2D> icons = new();

  /// <summary>Raised when new icons became available.</summary>
  public static event Action? IconsReady;

  public static Texture2D? IconFor(int itemId)
  {
    return icons.TryGetValue(itemId, out var icon) ? icon : null;
  }

  public override async void _Ready()
  {
    if (DisplayServer.GetName() == "headless")
    {
      return;
    }

    var missing = new List<ItemDef>();

    foreach (var def in ItemRegistry.All)
    {
      if (def.Model != null && !icons.ContainsKey(def.NetId))
      {
        missing.Add(def);
      }
    }

    if (missing.Count == 0)
    {
      return;
    }

    var viewport = new SubViewport
    {
      Size = new Vector2I(IconPixels, IconPixels),
      TransparentBg = true,
      OwnWorld3D = true,
      Msaa3D = Viewport.Msaa.Msaa4X,
      RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
    };

    var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Position = new Vector3(0f, 0f, 3f), Current = true };
    var light = new DirectionalLight3D { RotationDegrees = new Vector3(-35f, -30f, 0f), LightEnergy = 1.1f };

    var environment = new WorldEnvironment
    {
      Environment = new Godot.Environment
      {
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = Colors.White,
        AmbientLightEnergy = 0.55f,
      },
    };

    AddChild(viewport);
    viewport.AddChild(camera);
    viewport.AddChild(light);
    viewport.AddChild(environment);

    foreach (var def in missing)
    {
      var model = def.Model!;
      var node = model.Build();
      node.RotationDegrees = model.IconRotationDegrees;
      viewport.AddChild(node);
      Frame(node, camera);

      viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
      await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
      await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

      var image = viewport.GetTexture().GetImage();

      if (image != null)
      {
        icons[def.NetId] = ImageTexture.CreateFromImage(image);
      }

      viewport.RemoveChild(node);
      node.Free();
    }

    viewport.QueueFree();
    IconsReady?.Invoke();
  }

  /// <summary>Centres the model on the origin and sizes the orthographic camera to fit it, whatever its shape.</summary>
  static void Frame(Node3D node, Camera3D camera)
  {
    Aabb? bounds = null;

    foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
    {
      var mesh = (MeshInstance3D)child;
      var box = mesh.GlobalTransform * mesh.GetAabb();
      bounds = bounds == null ? box : bounds.Value.Merge(box);
    }

    if (node is MeshInstance3D self)
    {
      var box = self.GlobalTransform * self.GetAabb();
      bounds = bounds == null ? box : bounds.Value.Merge(box);
    }

    if (bounds == null)
    {
      camera.Size = 1f;

      return;
    }

    var center = bounds.Value.GetCenter();
    node.Position -= new Vector3(center.X, center.Y, 0f);
    camera.Size = Mathf.Max(bounds.Value.Size.X, bounds.Value.Size.Y) * 1.2f;
  }
}
