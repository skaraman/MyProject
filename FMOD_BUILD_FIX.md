# FMOD Build Crash Fix - SOLUTION FOR NATIVE CRASH

⚠️ **CRITICAL**: This fix resolves the "Native Crash Reporting" error during Unity builds.

## Problem
The Unity build process crashes with a native mono runtime error because the required FMOD release libraries are missing for Windows and Linux platforms.

**Error Message:**
```
Native Crash Reporting
Got a UNKNOWN while executing native code. This usually indicates
a fatal error in the mono runtime or one of the native libraries 
used by your application.

Managed Stacktrace:
  at UnityEditor.BuildPipeline:BuildPlayerInternalNoCheck_Injected
  at DefaultBuildMethods:BuildPlayer
```

## Root Cause
The FMOD plugin folder (`Assets/Plugins/FMOD/platforms/`) contains only development/logging versions of the native libraries:
- **Windows**: Only has `fmodstudioL.dll` (missing `fmod.dll` and `fmodstudio.dll`)
- **Linux**: Only has `libfmodstudioL.so` (missing `libfmod.so` and `libfmodstudio.so`)
- **Mac**: ✅ Has both development and release versions

The 'L' suffix indicates logging/development libraries. When Unity builds a player for Windows or Linux, it cannot find the required release versions, resulting in a native crash.

## Quick Check
Run the automated checker tool in Unity Editor:
1. Open Unity Editor
2. Go to menu: **Tools > FMOD > Check Release Libraries**
3. The tool will list all missing libraries

## Solution

### Step 1: Download FMOD Libraries
You need to download the FMOD Engine release libraries for your target platforms:

1. Visit: https://www.fmod.com/download
2. Download **FMOD Engine** (not FMOD Studio) for your FMOD version (2.02.08)
   - **FMOD Engine** provides the runtime libraries needed to play audio in your built game
   - **FMOD Studio** is the authoring tool (you already have integration files)
3. You need downloads for:
   - Windows
   - Linux

### Step 2: Extract Required Libraries

#### For Windows (x86_64):
From the FMOD Engine download, extract and copy to `Assets/Plugins/FMOD/platforms/win/lib/x86_64/`:
- `fmod.dll` (FMOD Core Engine - provides the low-level audio runtime)
- `fmodstudio.dll` (FMOD Studio Runtime - provides the high-level Studio API for events and banks)

#### For Windows (x86) - if building 32-bit:
From the FMOD Engine download, extract and copy to `Assets/Plugins/FMOD/platforms/win/lib/x86/`:
- `fmod.dll`
- `fmodstudio.dll`

#### For Linux (x86_64):
From the FMOD Engine download, extract and copy to `Assets/Plugins/FMOD/platforms/linux/lib/x86_64/`:
- `libfmod.so` (FMOD Core Engine - provides the low-level audio runtime)
- `libfmodstudio.so` (FMOD Studio Runtime - provides the high-level Studio API for events and banks)

### Step 3: Configure Import Settings
For each new DLL/SO file added, Unity should auto-generate `.meta` files. If not, or if you need to manually configure:

#### Windows DLL Import Settings:
- Platform: Windows (Win/Win64)
- CPU: x86_64 (or x86 for 32-bit)
- Editor: Disabled (use development versions in editor)
- Standalone: Enabled

#### Linux SO Import Settings:
- Platform: Linux (Linux64)
- CPU: x86_64
- Editor: Disabled (use development versions in editor)
- Standalone: Enabled

### Step 4: Verify File Structure
After adding the files, your structure should look like:

```
Assets/Plugins/FMOD/platforms/
├── win/
│   └── lib/
│       ├── x86/
│       │   ├── fmod.dll                    [NEW - Release]
│       │   ├── fmodstudio.dll              [NEW - Release]
│       │   └── fmodstudioL.dll             [Existing - Dev]
│       └── x86_64/
│           ├── fmod.dll                    [NEW - Release]
│           ├── fmodstudio.dll              [NEW - Release]
│           └── fmodstudioL.dll             [Existing - Dev]
├── linux/
│   └── lib/
│       └── x86_64/
│           ├── libfmod.so                  [NEW - Release]
│           ├── libfmodstudio.so            [NEW - Release]
│           └── libfmodstudioL.so           [Existing - Dev]
└── mac/
    └── lib/
        ├── fmodstudio.bundle               [Existing - Release]
        ├── fmodstudioL.bundle              [Existing - Dev]
        └── resonanceaudio.bundle           [Existing]
```

### Step 5: Test Build
1. Open your Unity project
2. Go to File > Build Settings
3. Select your target platform (Windows or Linux)
4. Click "Build" to verify the crash is resolved

## Alternative Solution: Disable FMOD for Build Testing
If you don't use FMOD audio in your project or want to test the build without FMOD:

1. Temporarily rename `Assets/Plugins/FMOD` to `Assets/Plugins/FMOD_DISABLED`
2. Remove any FMOD-related code from your scripts
3. Attempt the build again

## Notes
- Keep the development versions (fmodstudioL.dll/libfmodstudioL.so) as they are used in the Unity Editor
- The release versions are required only for built players
- Ensure you use libraries matching your FMOD version (2.02.08)
- The FMOD libraries are not included in version control as they are proprietary

## Reference
- FMOD Version: 2.02.08 (as detected from FMODStudioSettings.asset)
- Missing files cause Unity's BuildPipeline to crash during native library loading
