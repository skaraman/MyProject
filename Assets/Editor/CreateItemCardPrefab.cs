using UnityEditor;
using UnityEngine;
using Esperanza.UI;
using System.IO;

public class CreateItemCardPrefab
{
    [MenuItem("Tools/Generate ItemCard Prefab")]
    public static void GeneratePrefab()
    {
        string dir = "Assets/Prefabs/UI";
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        GameObject root = new GameObject("ItemCard");
        ItemCard itemCard = root.AddComponent<ItemCard>();
        
        Material uiCommonMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/UIcommon.mat");

        // Create Backgrounds
        itemCard.backgroundImage = CreateImageChild(root, "Background", "Assets/Sprites/Items/Card/Basic/Pieces/Background1.png", uiCommonMaterial);
        itemCard.innerBackgroundImage = CreateImageChild(root, "InnerBackground", "Assets/Sprites/Items/Card/Basic/Pieces/Background2.png", uiCommonMaterial);
        
        // Create Frames
        itemCard.frameOuterImage = CreateImageChild(root, "FrameOuter", "Assets/Sprites/Items/Card/Basic/Pieces/FrameOuter.png", uiCommonMaterial);
        itemCard.frameInnerImage = CreateImageChild(root, "FrameInner", "Assets/Sprites/Items/Card/Basic/Pieces/FrameInner.png", uiCommonMaterial);
        
        // Create Meters
        itemCard.soulMeterFill = CreateImageChild(root, "SoulMeter", "Assets/Sprites/Items/Card/Basic/Pieces/SoulMeter.png", uiCommonMaterial);
        itemCard.boostMeterFill = CreateImageChild(root, "BoostMeter", "Assets/Sprites/Items/Card/Basic/Pieces/BoostMeter.png", uiCommonMaterial);
        
        // Create Backings
        itemCard.nameBackingImage = CreateImageChild(root, "NameBacking", "Assets/Sprites/Items/Card/Basic/Pieces/NameBacking.png", uiCommonMaterial);
        itemCard.imageBackingImage = CreateImageChild(root, "ImageBacking", "Assets/Sprites/Items/Card/Basic/Pieces/ImageBacking.png", uiCommonMaterial);
        itemCard.embellishmentImage = CreateImageChild(root, "Embellishment", "Assets/Sprites/Items/Card/Basic/Pieces/Embellishment.png", uiCommonMaterial);
        
        // Icon
        itemCard.itemIconImage = CreateImageChild(root, "ItemIcon", null, uiCommonMaterial);
        
        // Texts
        itemCard.categoryText = CreateTextChild(root, "CategoryText");
        itemCard.itemNameText = CreateTextChild(root, "ItemNameText");
        itemCard.itemSlotText = CreateTextChild(root, "ItemSlotText");
        itemCard.damageText = CreateTextChild(root, "DamageText");
        itemCard.attackSpeedText = CreateTextChild(root, "AttackSpeedText");
        itemCard.durabilityText = CreateTextChild(root, "DurabilityText");

        string prefabPath = dir + "/ItemCard.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        GameObject.DestroyImmediate(root);
        Debug.Log("ItemCard Prefab generated successfully at " + prefabPath);
        
        // Ping the object in the project window
        Object obj = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);
        if (obj != null) EditorGUIUtility.PingObject(obj);
    }

    static SpriteRenderer CreateImageChild(GameObject parent, string name, string spritePath, Material mat)
    {
        GameObject go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(parent.transform, false);
        SpriteRenderer img = go.GetComponent<SpriteRenderer>();
        if (mat != null) img.sharedMaterial = mat;
        if (!string.IsNullOrEmpty(spritePath))
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite != null) img.sprite = sprite;
        }
        return img;
    }

    static FontText CreateTextChild(GameObject parent, string name)
    {
        GameObject go = new GameObject(name, typeof(FontText));
        go.transform.SetParent(parent.transform, false);
        return go.GetComponent<FontText>();
    }
}
