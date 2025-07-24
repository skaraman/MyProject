using UnityEngine;

public class FireMaterialPreset : MonoBehaviour
{
    [ContextMenu("Apply Fire Preset")]
    void ApplyFirePreset()
    {
        var shader = Shader.Find("AllIn1SpriteShader/AllIn1Urp2dRenderer");
        if (shader == null)
        {
            Debug.LogError("Shader not found.");
            return;
        }

        var material = new Material(shader);

        material.EnableKeyword("GLOW_ON");
        material.EnableKeyword("GLOWLIGHT_ON");
        material.EnableKeyword("DISTORT_ON");
        material.EnableKeyword("WAVEUV_ON");
        material.EnableKeyword("FLICKER_ON");
        material.EnableKeyword("GRADIENT2COL_ON");
        material.EnableKeyword("SHINE_ON");
        material.EnableKeyword("ALPHAOUTLINE_ON");

        material.SetColor("_GlowColor", new Color(1f, 0.6f, 0.1f)); // orange glow
        material.SetFloat("_GlowIntensity", 3.5f);

        material.SetColor("_GradientColorA", Color.white);           // hot center
        material.SetColor("_GradientColorB", new Color(1f, 0.2f, 0f)); // red-orange edge

        material.SetFloat("_WaveSpeed", 2.2f);
        material.SetFloat("_WaveAmplitude", 0.08f);

        material.SetFloat("_DistortStrength", 0.06f);

        material.SetFloat("_FlickerSpeed", 3.0f);
        material.SetFloat("_FlickerIntensityMin", 0.7f);
        material.SetFloat("_FlickerIntensityMax", 1.2f);

        material.SetColor("_AlphaOutlineColor", new Color(1f, 0.8f, 0.4f));
        material.SetFloat("_AlphaOutlineSize", 0.02f);

        material.SetFloat("_ShineSpeed", 0.8f);
        material.SetFloat("_ShineWidth", 0.4f);

        var renderer = GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        Debug.Log("🔥 Fire preset applied to material and renderer.");
    }
}
