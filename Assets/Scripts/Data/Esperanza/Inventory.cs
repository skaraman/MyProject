
using System.Collections.Generic;

public static class Inventory {
  public static List<GearItem> Gear { set; get; }
  public static List<ConsumableItem> Consumables { set; get; }
  public static List<QuestItem> Quest { set; get; }
  public static List<GemItem> Gems { set; get; }
}