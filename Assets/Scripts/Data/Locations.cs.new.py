import re
from pathlib import Path

# Read the current file content (which appears to be single-line with escaped \n)
content = """using System; using UnityEngine;\n\n// Data-only location configuration - no runtime logic, GameObject refs stored as paths only\n[Serializable] public partial struct EnemySpawnRule(int maxEnemies,float spawnInterval) : IComparable<EnemySpawnRule> { int MaxCount=maxEnemies ; float IntervalSeconds=spawnInterval;} \npublic enum LocationObjectiveType{ FinalKillCount=0,SurvivalTimeSeconds=1 }\n// Pure data: enemy prefab path (Addressable key or AssetDatabase GUID), not a GameObject reference\n[Serializable] public class EnemyPrefabConfig([SerializeField]string assetPath=\"\"){} [System.Serializable][PartialEnum(LocationEnemyData.Locations)] partial struct DomeCity : 0 {}\npublic static class LocationObjectiveTypeExtensions{ int GetTargetValue(int typeIndex){ return (typeIndex==LocationObjectiveType.FinalKillCount) ? -1:254u;}} \n[Serializable] internal partial struct EnemySpawnRuleConfig([SerializeField]int maxAlive=0,[Min(1)][SerializeField]int level=1,List<DemonStatModifier> statBonuses=null!){}\n[System.Serializable][PartialEnum(LocationEnemyData.Locations)]partial class Custom:2{}"""

# Convert escaped \n to actual newlines
content = content.replace('\\n', '\n')

print("Current file structure:")
for i, line in enumerate(content.split('\n'), 1):
    print(f"{i}: {line}")