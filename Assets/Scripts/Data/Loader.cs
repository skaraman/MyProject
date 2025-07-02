// using System;
// using System.Collections.Generic;
// using UnityEngine;

// public class Loader
// {
//   public SaveLoad saveLoad = new SaveLoad();
//   public static void ConvertToForms(dynamic anyDict)
//   {
//     if (anyDict is IDictionary<string, object> dataDict)
//     {
//       EsperanzaForms.Active.Clear();
//       if (dataDict["Active"] is IDictionary<string, object> activeObj)
//       {
//         foreach (var kvp in activeObj)
//         {
//           int value = Convert.ToInt32(kvp.Value);
//           EsperanzaForms.Active[kvp.Key] = value;
//         }
//       }
//     }
//     else
//     {
//       DebugHelper.LogObject(EsperanzaForms.Active);
//     }
//     if (anyDict.data is IDictionary<string, object> innerData && innerData.ContainsKey("Unlocked"))
//     {
//       EsperanzaForms.Unlocked.Clear();
//       if (innerData["Unlocked"] is IDictionary<string, object> unlockedObj)
//       {
//         foreach (var kvp in unlockedObj)
//         {
//           int value = Convert.ToInt32(kvp.Value);
//           EsperanzaForms.Unlocked[kvp.Key] = value;
//         }
//       }
//     }
//     else
//     {
//       DebugHelper.LogObject(EsperanzaForms.Unlocked);
//     }
//   }
//   public void LoadForms()
//   {
//     var data = saveLoad.Read<Dictionary<string, object>>("Resources/Forms");
//     if (data == null)
//     {
//       SaveForms();
//     }
//     else
//     {
//       ConvertToForms(data);
//     }
//   }
//   public void SaveForms()
//   {

//     DictionaryWrapper<string, int> activeWrap = new DictionaryWrapper<string, int>(EsperanzaForms.Active);
//     DictionaryWrapper<string, int> unlockedWrap = new DictionaryWrapper<string, int>(EsperanzaForms.Unlocked);

//     DictionaryWrapper<string, object> wrapper = new DictionaryWrapper<string, object>
//     {
//       { "Active", activeWrap }, { "Unlocked", unlockedWrap }
//     };
//     saveLoad.Write<string, object>("Resources/Forms", wrapper);
//   }

//   public static void ConvertToStats(dynamic anyDict)
//   {
//     FormStatsValues.values.Clear();

//     // Loop through each group in the dynamic object assuming a structure like:
//     // { "data": { "<groupKey>": { "<statKey>": value, ... }, ... } }
//     foreach (var key in anyDict.data.Keys)
//     {
//       if (anyDict.data[key] is Dictionary<string, object> statsDict)
//       {
//         Dictionary<string, int> statGroup = new Dictionary<string, int>();
//         foreach (var stat in statsDict)
//         {
//           try
//           {
//             int statValue = Convert.ToInt32(stat.Value);
//             statGroup[stat.Key] = statValue;
//           }
//           catch (Exception ex)
//           {
//             Debug.LogError($"Error converting stat for group '{key}' key '{stat.Key}': {ex.Message}");
//           }
//         }
//         FormStatsValues.values[key] = statGroup;
//       }
//       else
//       {
//         Debug.LogWarning($"Stats group '{key}' is not a valid dictionary.");
//       }
//     }
//   }
//   public void LoadStats()
//   {
//     var data = saveLoad.Read<Dictionary<string, object>>("Resources/Stats");
//     if (data == null)
//     {
//       SaveStats();
//     }
//     else
//     {
//       ConvertToStats(data);
//     }
//   }
//   public void SaveStats()
//   {
//     DictionaryWrapper<string, int> baseWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Base"]);
//     DictionaryWrapper<string, int> boltWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Bolt"]);
//     DictionaryWrapper<string, int> fireWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Fire"]);
//     DictionaryWrapper<string, int> coldWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Cold"]);
//     DictionaryWrapper<string, int> aquaWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Aqua"]);
//     DictionaryWrapper<string, int> darkWrapper = new DictionaryWrapper<string, int>(FormStatsValues.values["Dark"]);

//     DictionaryWrapper<string, object> wrapper = new DictionaryWrapper<string, object>
//     {
//       { "Base", baseWrapper }, { "Bolt", boltWrapper }, { "Cold", coldWrapper },
//       { "Fire", fireWrapper }, { "Cold", coldWrapper }, { "Dark", darkWrapper },
//     };
//     saveLoad.Write<string, object>("Resources/Stats", wrapper);
//   }

//   public void ConvertToInventory(Dictionary<string, object> anyDict)
//   {
//     Inventory.Gear = new List<GearItem>();
//     Inventory.Consumables = new List<ConsumeableItem>();
//     Inventory.Quest = new List<QuestItem>();
//     Inventory.Gems = new List<GemItem>();

//     // Process Gear
//     if (anyDict.TryGetValue("Gear", out var gearObj) && gearObj is List<object> gearList)
//     {
//       foreach (var obj in gearList)
//       {
//         if (obj is Dictionary<string, object> gearDict)
//         {
//           try
//           {
//             GearItem item = new GearItem
//             {
//               boosts = new List<BoostEntry>(),
//               type = gearDict.GetValueOrDefault("type")?.ToString(),
//               name = gearDict.GetValueOrDefault("name")?.ToString(),
//               gearId = gearDict.GetValueOrDefault("gearId")?.ToString(),
//               slot = gearDict.GetValueOrDefault("slot")?.ToString(),
//               gearColor = gearDict.GetValueOrDefault("gearColor")?.ToString(),
//             };

//             if (gearDict.TryGetValue("boosts", out var boostsObj) && boostsObj is List<object> boostsList)
//             {
//               foreach (var boostObj in boostsList)
//               {
//                 if (boostObj is Dictionary<string, object> boostDict)
//                 {
//                   BoostEntry boost = new BoostEntry
//                   {
//                     statName = boostDict.GetValueOrDefault("statName")?.ToString(),
//                     value = Convert.ToSingle(boostDict.GetValueOrDefault("value"))
//                   };
//                   item.boosts.Add(boost);
//                 }
//               }
//             }

//             Inventory.Gear.Add(item);
//           }
//           catch (Exception ex)
//           {
//             Debug.LogError($"Error processing a Gear item: {ex.Message}");
//           }
//         }
//       }
//     }

//     // Process Consumables
//     if (anyDict.TryGetValue("Consumables", out var consumablesObj) && consumablesObj is List<object> consumablesList)
//     {
//       foreach (var itemObj in consumablesList)
//       {
//         if (itemObj is Dictionary<string, object> itemDict)
//         {
//           var item = new ConsumeableItem
//           {
//             // Fill fields manually based on your class
//             // e.g. item.name = itemDict["name"].ToString();
//           };
//           Inventory.Consumables.Add(item);
//         }
//       }
//     }

//     // Process Quest
//     if (anyDict.TryGetValue("Quest", out var questObj) && questObj is List<object> questList)
//     {
//       foreach (var itemObj in questList)
//       {
//         if (itemObj is Dictionary<string, object> itemDict)
//         {
//           var item = new QuestItem
//           {
//             // Fill fields manually
//           };
//           Inventory.Quest.Add(item);
//         }
//       }
//     }

//     // Process Gems
//     if (anyDict.TryGetValue("Gems", out var gemsObj) && gemsObj is List<object> gemsList)
//     {
//       foreach (var itemObj in gemsList)
//       {
//         if (itemObj is Dictionary<string, object> itemDict)
//         {
//           var item = new GemItem
//           {
//             // Fill fields manually
//           };
//           Inventory.Gems.Add(item);
//         }
//       }
//     }
//   }
//   public static DictionaryWrapper<string, object> ConvertFromInventory()
//   {
//     var inventoryWrapper = new DictionaryWrapper<string, object>();
//     // Convert Gear items
//     List<object> gearList = new List<object>();
//     foreach (var gear in Inventory.Gear)
//     {
//       var gearWrapper = new DictionaryWrapper<string, object>
//       {
//         { "type", gear.type }, { "name", gear.name }, { "gearId", gear.gearId },
//         { "slot", gear.slot }, { "gearColor", gear.gearColor }
//       };
//       List<object> boostsList = new List<object>();
//       if (gear.boosts != null)
//       {
//         foreach (var boost in gear.boosts)
//         {
//           var boostWrapper = new DictionaryWrapper<string, object>
//           {
//             { "statName", boost.statName },
//             { "value", boost.value }
//           };
//           boostsList.Add(boostWrapper);
//         }
//       }
//       gearWrapper["boosts"] = boostsList;
//       gearList.Add(gearWrapper);
//     }
//     inventoryWrapper["Gear"] = gearList;
//     inventoryWrapper["Consumables"] = Inventory.Consumables;
//     inventoryWrapper["Quest"] = Inventory.Quest;
//     inventoryWrapper["Gems"] = Inventory.Gems;
//     return inventoryWrapper;
//   }
//   public void LoadInventory()
//   {
//     var data = saveLoad.Read<Dictionary<string, object>>("Resources/Inventory");
//     if (data == null)
//     {
//       SaveInventory();
//     }
//     else
//     {
//       ConvertToInventory(data);
//     }
//   }
//   public void SaveInventory()
//   {
//     Dictionary<string, object> anyDictionaryInvt = ConvertFromInventory();
//     saveLoad.Write<string, object>("Resources/Inventory", anyDictionaryInvt);
//   }

//   public void ConvertToEquip(Dictionary<string, object> anyDict)
//   {
//     foreach (var formKey in anyDict.Keys)
//     {
//       if (anyDict[formKey] is Dictionary<string, object> formDict)
//       {
//         Dictionary<string, GearItem> formEquip = new Dictionary<string, GearItem>();
//         foreach (var slotKvp in formDict)
//         {
//           if (slotKvp.Value is Dictionary<string, object> gearDict)
//           {
//             try
//             {
//               GearItem item = new GearItem
//               {
//                 boosts = new List<BoostEntry>(),
//                 type = gearDict.GetValueOrDefault("type")?.ToString(),
//                 name = gearDict.GetValueOrDefault("name")?.ToString(),
//                 gearId = gearDict.GetValueOrDefault("gearId")?.ToString(),
//                 gearColor = gearDict.GetValueOrDefault("gearColor")?.ToString(),
//                 slot = gearDict.ContainsKey("slot") ? gearDict["slot"].ToString() : slotKvp.Key
//               };

//               if (gearDict.TryGetValue("boosts", out var boostsObj) && boostsObj is List<object> boostList)
//               {
//                 foreach (var boostObj in boostList)
//                 {
//                   if (boostObj is Dictionary<string, object> boostDict)
//                   {
//                     BoostEntry boost = new BoostEntry
//                     {
//                       statName = boostDict.GetValueOrDefault("statName")?.ToString(),
//                       value = Convert.ToSingle(boostDict.GetValueOrDefault("value"))
//                     };
//                     item.boosts.Add(boost);
//                   }
//                 }
//               }

//               formEquip[slotKvp.Key] = item;
//             }
//             catch (Exception ex)
//             {
//               Debug.LogError($"Error processing equipment for form '{formKey}' at slot '{slotKvp.Key}': {ex.Message}");
//             }
//           }
//         }
//         EquippedItems.AllGearForms[formKey] = formEquip;
//       }
//     }
//   }
//   public static Dictionary<string, object> ConvertFromEquip()
//   {
//     var anyDictionary = new Dictionary<string, object>();
//     foreach (var form in EquippedItems.AllGearForms)
//     {
//       Dictionary<string, object> formDict = new Dictionary<string, object>();
//       foreach (var slot in form.Value)
//       {
//         GearItem item = slot.Value;
//         if (item == null)
//         {
//           continue;
//         }
//         Dictionary<string, object> gearDict = new Dictionary<string, object>
//         {
//           { "type", item.type }, { "name", item.name }, { "gearId", item.gearId },
//           { "gearColor", item.gearColor }, { "slot", item.slot }
//         };
//         List<object> boostsList = new List<object>();
//         if (item.boosts != null)
//         {
//           foreach (var boost in item.boosts)
//           {
//             Dictionary<string, object> boostDict = new Dictionary<string, object>
//             {
//               { "statName", boost.statName }, { "value", boost.value }
//             };
//             boostsList.Add(boostDict);
//           }
//         }
//         gearDict["boosts"] = boostsList;
//         formDict[slot.Key] = gearDict;
//       }
//       anyDictionary[form.Key] = formDict;
//     }
//     return anyDictionary;
//   }
//   public void LoadEquip()
//   {
//     var data = saveLoad.Read<Dictionary<string, object>>("Resources/EquippedItems");
//     if (data == null)
//     {
//       SaveEquip();
//     }
//     else
//     {
//       ConvertToEquip(data);
//     }
//   }
//   public void SaveEquip()
//   {
//     Dictionary<string, object> anyDictionaryEquip = ConvertFromEquip();
//     saveLoad.Write("Resources/EquippedItems", anyDictionaryEquip);
//   }
// }
