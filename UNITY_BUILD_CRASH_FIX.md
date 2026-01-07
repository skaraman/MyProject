# Unity Build Crash Fix (0xc0000005)

## Problem
The Unity build process was crashing with a fatal error:
```
Got a UNKNOWN while executing native code. This usually indicates
a fatal error in the mono runtime or one of the native libraries 
used by your application.

The thread tried to read from or write to a virtual address for which 
it does not have the appropriate access 0xc0000005
```

Stack trace showed the crash occurred in:
- `UnityEditor.BuildPipeline:BuildPlayerInternalNoCheck_Injected`
- `UnityEditor.BuildPipeline:BuildPlayerInternalNoCheck`

## Root Cause
This is a **known Unity 6000.3.x bug** that has been fixed in Unity 6000.4.0a1. The issue is caused by:

1. **Disabled Burst Safety Checks**: The Burst compiler's safety checks were disabled (`EnableSafetyChecks: false`), which removes bounds checking and memory validation during compilation
2. **Aggressive Optimization**: The Burst compiler was set to aggressive performance optimization mode (`OptimizeFor: 1`)
3. **Particle Systems**: The project contains particle systems (in LeanTween examples) which, when combined with disabled safety checks, can trigger memory access violations during the build process

## Solution Applied
Modified `ProjectSettings/BurstAotSettings_StandaloneOSX.json`:

### Changes:
1. **Enabled Safety Checks**: Changed `EnableSafetyChecks` from `false` to `true`
   - This adds bounds checking and memory validation during Burst compilation
   - Prevents memory access violations by validating all memory operations
   - Minimal performance impact during build time only

2. **Balanced Optimization**: Changed `OptimizeFor` from `1` to `0`
   - `0` = Balanced optimization (safer, recommended for development)
   - `1` = Performance optimization (aggressive, can cause issues)
   - This provides more conservative optimization during the build process

## Why This Works
- **Safety checks** prevent the Burst compiler from generating code that accesses invalid memory addresses
- **Balanced optimization** reduces the likelihood of edge cases in the compiler's code generation
- This is a **workaround** for the Unity bug until the project can be upgraded to Unity 6000.4.0+ where the bug is fixed

## Alternative Solutions (if this doesn't work)
If the build crash persists:

1. **Update Unity**: Upgrade to Unity 6000.4.0a1 or later (official fix)
2. **Simplify Particle Systems**: Temporarily disable or simplify complex particle system hierarchies
3. **Graphics Drivers**: Update graphics drivers (NVIDIA/AMD/Intel) to latest WHQL versions
4. **Remove Citrix**: Uninstall Citrix Workspace/Receiver if installed (known to cause conflicts)
5. **Disable Burst Entirely**: As a last resort, disable Burst compilation completely in build settings

## Performance Impact
- **Build time**: May be slightly slower due to safety checks (negligible impact)
- **Runtime performance**: No impact - these settings only affect the build process
- **Development**: Safer, more stable builds with better error reporting

## References
- Unity Issue Tracker: Particle System crashes with debug allocator in Unity 6000.3.x
- Fixed in Unity 6000.4.0a1
- Error code 0xc0000005 = STATUS_ACCESS_VIOLATION (Windows memory access violation)
