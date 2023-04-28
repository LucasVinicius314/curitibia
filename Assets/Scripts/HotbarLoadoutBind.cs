#nullable enable

enum HotbarLoadoutBindType
{
  key,
  mouse,
}

class HotbarLoadoutBind
{
  Item? item;

  ItemAction? itemAction;

  HotbarLoadoutBindType type;
}
