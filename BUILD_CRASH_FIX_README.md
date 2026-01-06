# Build Crash Fix - README

## Issue
Your Unity project is experiencing a **Native Crash** during the build process. This is caused by missing FMOD audio library files.

## Quick Fix
Follow these steps to resolve the crash:

### 1. Verify the Problem
Open Unity and run: **Tools > FMOD > Check Release Libraries**

This will show you exactly which files are missing.

### 2. Get the Missing Files
See **[FMOD_BUILD_FIX.md](./FMOD_BUILD_FIX.md)** for detailed instructions on:
- Where to download FMOD libraries
- Which files to download
- Where to place them in your project

### 3. Verify the Fix
After adding the files:
1. Run **Tools > FMOD > Check Release Libraries** again
2. All checks should pass
3. Try building your project

## Documentation Files

- **[FMOD_BUILD_FIX.md](./FMOD_BUILD_FIX.md)** - Complete fix instructions (START HERE)
- **[FMOD_LIBRARY_CONFIG_TEMPLATES.md](./FMOD_LIBRARY_CONFIG_TEMPLATES.md)** - Library import settings reference
- **Assets/Editor/FMODLibraryChecker.cs** - Automated checking tool

## What's Missing

Your project currently has:
- ✅ Mac: Complete FMOD libraries (release + development)
- ❌ Windows: Only development libraries
- ❌ Linux: Only development libraries

You need to add:
- Windows: `fmod.dll` and `fmodstudio.dll` (for x86 and x86_64)
- Linux: `libfmod.so` and `libfmodstudio.so` (for x86_64)

## Why This Happened

FMOD libraries are proprietary and cannot be stored in source control. The development versions work fine in the Unity Editor, but **builds require the release versions**. Someone who set up this project originally may have forgotten to document this requirement.

## Alternative: Remove FMOD

If you don't actually use FMOD audio in your project:
1. Rename `Assets/Plugins/FMOD` to `Assets/Plugins/FMOD_DISABLED`
2. Remove any FMOD-related code from your scripts
3. Try building again

---

**FMOD Version**: 2.02.08  
**Download**: https://www.fmod.com/download
