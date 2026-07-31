#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[FilePath("ProjectSettings/FormEffectPreviewDefaults.asset", FilePathAttribute.Location.ProjectFolder)]
public sealed class FormEffectPreviewDefaults : ScriptableSingleton<FormEffectPreviewDefaults> {
  [SerializeField] bool hasFireDefaults;
  [SerializeReference] IEffectPreviewDrawer fireDefaults;
  [SerializeField] bool hasAquaDefaults;
  [SerializeReference] IEffectPreviewDrawer aquaDefaults;
  [SerializeField] bool hasBoltDefaults;
  [SerializeReference] IEffectPreviewDrawer boltDefaults;
  [SerializeField] bool hasColdDefaults;
  [SerializeReference] IEffectPreviewDrawer coldDefaults;
  [SerializeField] bool hasDarkDefaults;
  [SerializeReference] IEffectPreviewDrawer darkDefaults;

  public bool ApplySavedDefaults(IEffectPreviewDrawer target) {
    var source = GetSavedDefaults(target);
    if (source == null) return false;

    target.CopySettingsFrom(source);
    return true;
  }

  public void SaveDefaults(IEffectPreviewDrawer source) {
    switch (source) {
      case AllIn1EffectPreviewDrawer:
        fireDefaults = CopyDrawer(source, new AllIn1EffectPreviewDrawer());
        hasFireDefaults = true;
        break;
      case AquaEffectPreviewDrawer:
        aquaDefaults = CopyDrawer(source, new AquaEffectPreviewDrawer());
        hasAquaDefaults = true;
        break;
      case BoltEffectPreviewDrawer:
        boltDefaults = CopyDrawer(source, new BoltEffectPreviewDrawer());
        hasBoltDefaults = true;
        break;
      case ColdEffectPreviewDrawer:
        coldDefaults = CopyDrawer(source, new ColdEffectPreviewDrawer());
        hasColdDefaults = true;
        break;
      case DarkEffectPreviewDrawer:
        darkDefaults = CopyDrawer(source, new DarkEffectPreviewDrawer());
        hasDarkDefaults = true;
        break;
      default:
        return;
    }

    Save(true);
    Debug.Log($"[{nameof(FormEffectPreviewDefaults)}] Saved {source.DisplayName} defaults");
  }

  IEffectPreviewDrawer GetSavedDefaults(IEffectPreviewDrawer target) {
    return target switch {
      AllIn1EffectPreviewDrawer when hasFireDefaults => fireDefaults,
      AquaEffectPreviewDrawer when hasAquaDefaults => aquaDefaults,
      BoltEffectPreviewDrawer when hasBoltDefaults => boltDefaults,
      ColdEffectPreviewDrawer when hasColdDefaults => coldDefaults,
      DarkEffectPreviewDrawer when hasDarkDefaults => darkDefaults,
      _ => null
    };
  }

  static IEffectPreviewDrawer CopyDrawer(IEffectPreviewDrawer source, IEffectPreviewDrawer destination) {
    destination.CopySettingsFrom(source);
    return destination;
  }
}
#endif
