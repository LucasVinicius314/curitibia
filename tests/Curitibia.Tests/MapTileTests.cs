using Curitibia.Core;
using Curitibia.Map;
using Curitibia.World;
using Xunit;

namespace Curitibia.Tests;

public class MapTileTests
{
  static ChunkData Flat(byte block, int height)
  {
    var blocks = new ChunkData();

    for (int z = 0; z < VoxelMath.ChunkSize; z++)
    {
      for (int x = 0; x < VoxelMath.ChunkSize; x++)
      {
        for (int y = 0; y < height; y++)
        {
          blocks.Set(x, y, z, block);
        }
      }
    }

    return blocks;
  }

  static (int r, int g, int b) Pixel(byte[] rgba, int x, int z)
  {
    var o = (x + z * MapTileBuilder.Size) * 4;

    return (rgba[o], rgba[o + 1], rgba[o + 2]);
  }

  [Fact]
  public void Top_height_is_the_layer_above_the_highest_block()
  {
    var blocks = Flat(BlockIds.Stone, 7);
    blocks.Set(3, 12, 4, BlockIds.Wood);

    Assert.Equal(7, MapTileBuilder.TopHeight(blocks, 0, 0));
    Assert.Equal(13, MapTileBuilder.TopHeight(blocks, 3, 4));
    Assert.Equal(0, MapTileBuilder.TopHeight(null, 0, 0));
    Assert.Equal(0, MapTileBuilder.TopHeight(new ChunkData(), 5, 5));
  }

  [Fact]
  public void A_flat_chunk_is_the_colour_of_its_top_block()
  {
    var rgba = new byte[MapTileBuilder.Bytes];
    MapTileBuilder.Render(Flat(BlockIds.Sand, 8), null, null, rgba);

    var (r, g, b) = Pixel(rgba, 10, 10);
    var (er, eg, eb) = MapTileBuilder.ColorOf(BlockIds.Sand);

    // The only change is the small altitude brightening, so the hue stays and every channel is within a few percent.
    Assert.InRange(r, er * 0.8, er * 1.3 + 1);
    Assert.InRange(g, eg * 0.8, eg * 1.3 + 1);
    Assert.InRange(b, eb * 0.8, eb * 1.3 + 1);
    Assert.Equal(255, rgba[3]);
  }

  [Fact]
  public void The_top_block_decides_the_colour_not_what_is_below()
  {
    var blocks = Flat(BlockIds.Stone, 8);
    blocks.Set(5, 8, 5, BlockIds.Leaves);
    var rgba = new byte[MapTileBuilder.Bytes];
    MapTileBuilder.Render(blocks, null, null, rgba);

    var (r, g, b) = Pixel(rgba, 5, 5);

    Assert.True(g > r && g > b, "a canopy over stone reads as green");
  }

  [Fact]
  public void Rising_ground_is_lighter_and_falling_ground_darker()
  {
    var blocks = Flat(BlockIds.Stone, 20);

    // A step up in the middle of the row z = 8: columns x >= 16 are 6 layers higher.
    for (int x = 16; x < VoxelMath.ChunkSize; x++)
    {
      for (int y = 20; y < 26; y++)
      {
        blocks.Set(x, y, 8, BlockIds.Stone);
      }
    }

    var rgba = new byte[MapTileBuilder.Bytes];
    MapTileBuilder.Render(blocks, null, null, rgba);

    var flat = Pixel(rgba, 5, 8).r;
    var risingEdge = Pixel(rgba, 16, 8).r;   // higher than the column to its west: lighter
    var fallingEdge = Pixel(rgba, 5, 9).r;    // z = 9 is level, but its north neighbour (z = 8) is the same height there: unchanged

    Assert.True(risingEdge > flat, $"{risingEdge} vs {flat}");
    Assert.Equal(flat, fallingEdge);

    // The column just after the step, to the north of a row that is lower: darker.
    Assert.True(Pixel(rgba, 16, 9).r < Pixel(rgba, 5, 9).r, "a column lower than the one to its north is darker");
  }

  [Fact]
  public void Border_columns_are_shaded_against_the_neighbouring_chunk()
  {
    var low = Flat(BlockIds.Stone, 10);
    var high = Flat(BlockIds.Stone, 24);
    var alone = new byte[MapTileBuilder.Bytes];
    var beside = new byte[MapTileBuilder.Bytes];

    MapTileBuilder.Render(low, null, null, alone);
    MapTileBuilder.Render(low, high, null, beside);

    Assert.True(Pixel(beside, 0, 5).r < Pixel(alone, 0, 5).r, "a tall chunk to the west puts its neighbour in shadow");
    Assert.Equal(Pixel(alone, 5, 5), Pixel(beside, 5, 5));
  }

  [Fact]
  public void An_empty_column_is_the_void_colour()
  {
    var rgba = new byte[MapTileBuilder.Bytes];
    MapTileBuilder.Render(new ChunkData(), null, null, rgba);

    Assert.Equal((16, 20, 30), Pixel(rgba, 3, 3));
  }
}
