# Parse Locations.cs content
import re, json
from pathlib import Path

cs_content = """
using System; using UnityEngine;
// Data-only location configuration - no runtime logic, GameObject refs stored as paths only  
[Serializable] public partial struct EnemySpawnRule(int maxEnemies,float spawnInterval) : IComparable<EnemySpawnRule> { int MaxCount=maxEnemies ; float IntervalSeconds=spawnInterval;}   
public enum LocationObjectiveType{ FinalKillCount=0,SurvivalTimeSeconds=1 }
// Pure data: enemy prefab path (Addressable key or AssetDatabase GUID), not a GameObject reference  
[Serializable] public class EnemyPrefabConfig([SerializeField]string assetPath=""){}
[System.Serializable][PartialEnum(LocationEnemyData.Locations)] partial struct DomeCity : 0 {}
public static class LocationObjectiveTypeExtensions{ int GetTargetValue(int typeIndex){ return (typeIndex==LocationObjectiveType.FinalKillCount) ? -1:254u;}}   
[Serializable] internal partial struct EnemySpawnRuleConfig([SerializeField]int maxAlive=0,[Min(1)][SerializeField]int level=1,List<DemonStatModifier> statBonuses=null!){}
[System.Serializable][PartialEnum(LocationEnemyData.Locations)]partial class Custom:2{}
"""

print("Parsed structure:")
for i, line in enumerate(cs_content.strip().split('\n'), 1):
    print(f"{i}: {line}" if len(line) > 0 else f"{i}:(empty)")