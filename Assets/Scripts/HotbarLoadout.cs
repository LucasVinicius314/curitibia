using System.Collections.Generic;

#nullable enable

class HotbarLoadout
{
  HandSlot? leftHand;
  HandSlot? rightHand;

  List<HotbarLoadoutBind> hotbarLoadoutBinds = new List<HotbarLoadoutBind>();
}
