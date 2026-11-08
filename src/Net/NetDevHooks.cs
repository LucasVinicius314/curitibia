using System;
using System.Globalization;
using System.Linq;
using Curitibia.Core;
using Curitibia.Enemies;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Net;

/// <summary>
/// Scripted two-process checks, active only with dev flags (after `--`):
///   --net-host / --net-join=CODE / --net-name=NAME / --net-url=URL / --net-no-friends
///   --net-edit=x,y,z,id     request one block edit 6 s into the world
///   --net-report-at=SEC     print `NET REPORT ...` lines (compare both sides), once, SEC s into the world
///   --net-quit-at=SEC       quit SEC s into the world (exit code 0)
/// Time counts from when the terrain exists, so it does not depend on how long the connection took.
/// </summary>
public partial class NetDevHooks : Node
{
  double elapsed;
  bool started;
  bool edited;
  bool reported;

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (terrain == null || !terrain.IsRunning)
    {
      return;
    }

    started = true;
    elapsed += delta;

    if (!edited && DevArgs.Get("net-edit") is { } edit && elapsed > 6.0)
    {
      edited = true;
      var p = edit.Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();
      GD.Print($"NET EDIT request {p[0]},{p[1]},{p[2]} id={p[3]}");
      terrain.RequestSetBlock(new Vector3I(p[0], p[1], p[2]), (byte)p[3]);
    }

    if (!reported && DevArgs.GetInt("net-report-at") is { } at && elapsed > at)
    {
      reported = true;
      Report(terrain);
    }

    if (DevArgs.GetInt("net-quit-at") is { } quit && elapsed > quit)
    {
      GD.Print("NET QUIT");
      GetTree().Quit(0);
    }
  }

  void Report(Terrain terrain)
  {
    var session = NetSession.Instance;
    var edits = terrain.SnapshotEdits().OrderBy(e => e.voxel.X).ThenBy(e => e.voxel.Y).ThenBy(e => e.voxel.Z).ToList();

    GD.Print($"NET REPORT kind={session.Kind} seed={terrain.Seed} edits={edits.Count} code={session.RoomCode}");

    foreach (var (voxel, id) in edits)
    {
      GD.Print($"NET REPORT edit {voxel.X},{voxel.Y},{voxel.Z}={id}");
    }

    if (GetTree().Root.FindChild("Players", true, false) is Node players)
    {
      foreach (var player in players.GetChildren().OfType<PlayerController>().OrderBy(p => p.Name.ToString()))
      {
        var pos = player.GlobalPosition;
        GD.Print($"NET REPORT player {player.Name} local={player.IsLocal} hp={player.Health.Hp:0} pos=({pos.X:0.0},{pos.Y:0.0},{pos.Z:0.0})");
      }
    }

    if (GetTree().Root.FindChild("Enemies", true, false) is Node enemies)
    {
      foreach (var enemy in enemies.GetChildren().OfType<Enemy>().OrderBy(e => e.Name.ToString()))
      {
        var pos = enemy.GlobalPosition;
        GD.Print($"NET REPORT enemy {enemy.Name} pos=({pos.X:0},{pos.Y:0},{pos.Z:0})");
      }
    }
  }
}
