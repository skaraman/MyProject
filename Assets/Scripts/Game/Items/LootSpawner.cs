using UnityEngine;
using System.Collections.Generic;

public static class LootSpawner {
  
  public static void DropLoot(Vector3 position, int maxHp) {
    // Always drop for now to ensure we can see it during testing
    
    // Determine the value of the drop based on max HP
    int minAmount = Mathf.Max(1, maxHp / 10);
    int maxAmount = maxHp * 10;
    int dropAmount = Random.Range(minAmount, maxAmount + 1);
    
    List<string> possibleDrops = new List<string>();
    possibleDrops.Add("Gold");
    
    if (EsperanzaForms.IsUnlocked("Base")) possibleDrops.Add("Base");
    if (EsperanzaForms.IsUnlocked("Dark")) possibleDrops.Add("Dark");
    if (EsperanzaForms.IsUnlocked("Bolt")) possibleDrops.Add("Bolt");
    if (EsperanzaForms.IsUnlocked("Cold")) possibleDrops.Add("Cold");
    if (EsperanzaForms.IsUnlocked("Fire")) possibleDrops.Add("Fire");
    if (EsperanzaForms.IsUnlocked("Aqua")) possibleDrops.Add("Aqua");
    
    string dropType = possibleDrops[Random.Range(0, possibleDrops.Count)];
    SpawnItem(position, dropType, dropAmount);
  }

  private static void SpawnItem(Vector3 position, string dropType, int amount) {
    string address = $"Assets/Prefabs/Items/LootDrop_{dropType}.prefab";
    
    // We try to load via RuntimeAssetCache first if it exists, otherwise fallback to Addressables
    GameObject prefab = null;
    if (RuntimeAssetCache.TryGetLoaded<GameObject>(address, out var loadedPrefab) && loadedPrefab != null) {
      prefab = loadedPrefab;
    } else {
#if UNITY_EDITOR
      prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(address);
      if (prefab == null) {
#endif
        try {
          var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(address);
          prefab = handle.WaitForCompletion();
        } catch (UnityEngine.AddressableAssets.InvalidKeyException) {
          Debug.LogWarning($"[LootSpawner] Addressable key not found for {address}. Please mark the prefab as Addressable!");
        }
#if UNITY_EDITOR
      }
#endif
    }

    if (prefab == null) {
      Debug.LogWarning($"[LootSpawner] Missing prefab for {dropType} at {address}!");
      return;
    }
    
    GameObject obj = Object.Instantiate(prefab, position, Quaternion.identity);
    
    // The physics are already set up on the prefab, just need to apply velocity
    if (obj.TryGetComponent<Rigidbody2D>(out var rb)) {
      float angle = Random.Range(60f, 120f) * Mathf.Deg2Rad;
      float force = Random.Range(5f, 10f);
      rb.linearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * force;
    }
    
    if (obj.TryGetComponent<LootPickup>(out var pickup)) {
      pickup.amount = amount;
    }
  }
}
