using System;
using System.Collections.Generic;
using Curitibia.Player;
using Curitibia.World;
using Godot;

namespace Curitibia.Core;

/// <summary>
/// A repeatable walk for measuring the game (dev flag --bench=SECONDS): once the world around the player has settled it carries
/// the local player in a straight line at a steady speed (--bench-speed, metres a second, default 7; --bench-yaw, degrees, default
/// 0 = along +X) just above the ground, and prints, every few seconds and at the end, the frame times (average, 99th percentile,
/// worst, how many frames hitched), what the main thread and the GPU spent, how many draw calls and triangles there were, and how
/// long every phase of the world pipeline took on the worker threads and on the main thread. Start it with --noclip so the player
/// is not pulled down while it moves them. Then it quits.
/// </summary>
public partial class PerfBench : Node
{
  readonly List<double> frames = new();
  readonly double[] lastMs = new double[Enum.GetValues<Phase>().Length];
  readonly long[] lastCount = new long[Enum.GetValues<Phase>().Length];

  double seconds;
  double speed = 7.0;
  double yaw;
  double elapsed;
  double window;
  bool running;
  Vector3 origin;
  PlayerController? player;

  public override void _Ready()
  {
    seconds = DevArgs.GetInt("bench") ?? 30;
    speed = DevArgs.GetInt("bench-speed") ?? 7;
    yaw = (DevArgs.GetInt("bench-yaw") ?? 0) * Math.PI / 180.0;
    RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
  }

  public override void _PhysicsProcess(double delta)
  {
    if (!running)
    {
      return;
    }

    if (player == null || !IsInstanceValid(player))
    {
      return;
    }

    var travelled = elapsed * speed;
    var x = origin.X + Math.Cos(yaw) * travelled;
    var z = origin.Z + Math.Sin(yaw) * travelled;
    var ground = Terrain.Instance?.GroundHeightAt(VoxelMath.WorldToVoxel((float)x), VoxelMath.WorldToVoxel((float)z)) ?? origin.Y;

    player.TeleportTo(new Vector3((float)x, ground + 2.5f, (float)z));
  }

  public override void _Process(double delta)
  {
    var terrain = Terrain.Instance;

    if (!running)
    {
      foreach (var candidate in PlayerController.All)
      {
        if (candidate.IsLocal)
        {
          player = candidate;
        }
      }

      if (player != null && terrain != null && terrain.IsSettled)
      {
        origin = player.GlobalPosition;
        running = true;
        Snapshot();
        GD.Print($"BENCH world settled {Time.GetTicksMsec() / 1000.0:F1} s after launch; start at {origin} walking {speed} m/s toward yaw {yaw * 180.0 / Math.PI:F0} for {seconds} s");
      }

      return;
    }

    elapsed += delta;
    window += delta;
    frames.Add(delta * 1000.0);

    // A frame that hitched: what the main thread spent in the world pipeline during it, and the engine's own process and physics time.
    var apply = WorldProfile.Milliseconds(Phase.Apply);
    var shape = WorldProfile.Milliseconds(Phase.ApplyShape);
    var mesh = WorldProfile.Milliseconds(Phase.ApplyMesh);
    var plan = WorldProfile.Milliseconds(Phase.Plan);

    if (delta * 1000.0 > 30.0 && hitches++ < 14)
    {
      GD.Print($"BENCH   hitch {delta * 1000.0:F0} ms: apply {apply - lastApply:F1} (mesh {mesh - lastMesh:F1}, shape {shape - lastShape:F1}) plan {plan - lastPlan:F1} | engine process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0:F1} physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0:F1} ms | gc2 {GC.CollectionCount(2)} pause {GC.GetTotalPauseDuration().TotalMilliseconds:F0} ms");
    }

    lastApply = apply;
    lastShape = shape;
    lastMesh = mesh;
    lastPlan = plan;

    if (window >= 5.0)
    {
      Report($"BENCH t={elapsed:F0}s", frames.GetRange(Math.Max(0, frames.Count - (int)(window * 60)), Math.Min(frames.Count, (int)(window * 60))), window);
      window = 0;
    }

    if (elapsed >= seconds)
    {
      Report("BENCH TOTAL", frames, elapsed);
      GetTree().Quit();
    }
  }

  int hitches;
  double lastApply, lastShape, lastMesh, lastPlan;
  int lastGc0, lastGc1, lastGc2;
  double lastPause;
  long lastVertices, lastTriangles, lastApplyCount;

  void Snapshot()
  {
    lastGc0 = GC.CollectionCount(0);
    lastGc1 = GC.CollectionCount(1);
    lastGc2 = GC.CollectionCount(2);
    lastPause = GC.GetTotalPauseDuration().TotalMilliseconds;
    lastVertices = WorldProfile.Vertices;
    lastTriangles = WorldProfile.CollisionTriangles;
    lastApplyCount = WorldProfile.Count(Phase.Apply);

    foreach (var phase in Enum.GetValues<Phase>())
    {
      lastMs[(int)phase] = WorldProfile.Milliseconds(phase);
      lastCount[(int)phase] = WorldProfile.Count(phase);
    }
  }

  void Report(string label, List<double> sample, double span)
  {
    if (sample.Count == 0)
    {
      return;
    }

    var sorted = new List<double>(sample);

    sorted.Sort();

    var average = 0.0;

    foreach (var f in sample)
    {
      average += f;
    }

    average /= sample.Count;

    var p99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99))];
    var worst = sorted[^1];
    var over20 = 0;
    var over40 = 0;

    foreach (var f in sample)
    {
      over20 += f > 20.0 ? 1 : 0;
      over40 += f > 40.0 ? 1 : 0;
    }

    var rid = GetViewport().GetViewportRid();
    var cpu = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0 + RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid);
    var gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
    var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
    var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1_000_000.0;
    var vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);
    var ram = System.Environment.WorkingSet / (1024.0 * 1024.0);

    GD.Print($"{label}: {sample.Count / span:F0} fps | frame avg {average:F1} p99 {p99:F1} max {worst:F1} ms | >20ms {over20} >40ms {over40} | cpu {cpu:F1} gpu {gpu:F1} ms | draws {draws:F0} tris {prims:F2}M | ram {ram:F0} vram {vram:F0} MB | chunks {Terrain.Instance?.LoadedChunkCount}");

    var line = "BENCH   phases (ms of work per second of wall time, calls/s, avg ms per call):";

    foreach (var phase in Enum.GetValues<Phase>())
    {
      var ms = WorldProfile.Milliseconds(phase) - lastMs[(int)phase];
      var calls = WorldProfile.Count(phase) - lastCount[(int)phase];

      if (calls > 0)
      {
        line += $"\nBENCH     {phase,-12} {ms / span,8:F1} ms/s  {calls / span,6:F1}/s  {ms / calls,7:F2} ms";
      }
    }

    var gc0 = GC.CollectionCount(0) - lastGc0;
    var gc1 = GC.CollectionCount(1) - lastGc1;
    var gc2 = GC.CollectionCount(2) - lastGc2;
    var pause = GC.GetTotalPauseDuration().TotalMilliseconds - lastPause;

    line += $"\nBENCH     GC           gen0 {gc0} gen1 {gc1} gen2 {gc2} in {span:F0} s, paused {pause:F0} ms | applied {(WorldProfile.Vertices - lastVertices) / Math.Max(1, WorldProfile.Count(Phase.Apply) - lastApplyCount)} vertices and {(WorldProfile.CollisionTriangles - lastTriangles) / Math.Max(1, WorldProfile.Count(Phase.Apply) - lastApplyCount)} collision triangles per chunk";

    GD.Print(line);
    Snapshot();
  }
}
