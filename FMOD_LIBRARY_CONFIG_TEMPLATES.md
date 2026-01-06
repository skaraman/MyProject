# FMOD Release Library Configuration Templates

These templates show the correct Unity import settings for FMOD release libraries.
Copy these configurations when you add the missing FMOD libraries.

## fmod.dll (Windows x86_64) - Template .meta file

```yaml
fileFormatVersion: 2
guid: <GENERATE_NEW_GUID>
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
    Win64:
      enabled: 1
      settings:
        CPU: x86_64
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## fmodstudio.dll (Windows x86_64) - Template .meta file

```yaml
fileFormatVersion: 2
guid: <GENERATE_NEW_GUID>
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Windows
    Win64:
      enabled: 1
      settings:
        CPU: x86_64
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## libfmod.so (Linux x86_64) - Template .meta file

```yaml
fileFormatVersion: 2
guid: <GENERATE_NEW_GUID>
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Linux
    Linux64:
      enabled: 1
      settings:
        CPU: x86_64
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## libfmodstudio.so (Linux x86_64) - Template .meta file

```yaml
fileFormatVersion: 2
guid: <GENERATE_NEW_GUID>
PluginImporter:
  externalObjects: {}
  serializedVersion: 3
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
    Any:
      enabled: 0
      settings: {}
    Editor:
      enabled: 0
      settings:
        CPU: x86_64
        DefaultValueInitialized: true
        OS: Linux
    Linux64:
      enabled: 1
      settings:
        CPU: x86_64
  userData: 
  assetBundleName: 
  assetBundleVariant: 
```

## Key Configuration Points

1. **Editor: enabled: 0** - Release libraries should NOT be used in the Unity Editor
2. **Win64/Linux64: enabled: 1** - Release libraries MUST be enabled for builds
3. **CPU: x86_64** - Specifies the architecture (use x86 for 32-bit builds)
4. **OS: Windows/Linux** - Platform specification

## Notes

- Unity will auto-generate .meta files when you add new DLL/SO files
- Verify the settings match these templates after import
- The development libraries (fmodstudioL.dll) should have Editor enabled
- Release libraries are used for standalone builds only
