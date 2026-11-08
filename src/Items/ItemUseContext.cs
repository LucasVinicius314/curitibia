using Curitibia.Player;
using Curitibia.World;

namespace Curitibia.Items;

/// <summary>What a behavior can act on when its item is used.</summary>
public readonly record struct ItemUseContext(PlayerController Player, Terrain? Terrain);
