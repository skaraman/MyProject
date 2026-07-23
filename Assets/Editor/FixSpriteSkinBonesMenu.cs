using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using System.Linq;

public class FixSpriteSkinBonesMenu
{
    [MenuItem("Tools/Fix SpriteSkin Bones in ESPER")]
    public static void Fix()
    {
        string path = "Assets/Prefabs/Characters/ESPER.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) {
            Debug.LogError("Prefab not found");
            return;
        }

        bool modified = false;
        SpriteSkin[] skins = prefab.GetComponentsInChildren<SpriteSkin>(true);
        foreach (SpriteSkin skin in skins)
        {
            SpriteRenderer sr = skin.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                var spriteBones = sr.sprite.GetBones();
                if (spriteBones != null && spriteBones.Length > 0)
                {
                    Transform[] boneTransforms = new Transform[spriteBones.Length];
                    
                    for (int i = 0; i < spriteBones.Length; i++)
                    {
                        string boneName = spriteBones[i].name;
                        Transform boneTransform = FindChildByName(prefab.transform, boneName);
                        boneTransforms[i] = boneTransform;
                    }
                    
                    SerializedObject so = new SerializedObject(skin);
                    SerializedProperty boneTransformsProp = so.FindProperty("m_BoneTransforms");
                    boneTransformsProp.arraySize = boneTransforms.Length;
                    for (int i = 0; i < boneTransforms.Length; i++)
                    {
                        boneTransformsProp.GetArrayElementAtIndex(i).objectReferenceValue = boneTransforms[i];
                    }
                    
                    if (boneTransforms[0] != null) {
                        var rootBoneProp = so.FindProperty("m_RootBone");
                        if (rootBoneProp != null) {
                            rootBoneProp.objectReferenceValue = boneTransforms[0].parent;
                        }
                    }

                    so.ApplyModifiedProperties();
                    modified = true;
                    Debug.Log("Fixed SpriteSkin on " + skin.gameObject.name);
                }
            }
        }

        if (modified)
        {
            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log("Successfully saved ESPER.prefab");
        }
    }

    static Transform FindChildByName(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindChildByName(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
