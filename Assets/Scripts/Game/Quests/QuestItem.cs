using System;

[Serializable]
public class QuestItem {
  public string Type { get; set; }
  public string Name { get; set; }
  public string IconLibrary { get; set; } = "Items/Items";
  public string IconCategory { get; set; }
  public string IconId { get; set; }
  public int Amount { get; set; }
}
