# Runtime Settings API - Wiki

Runtime Settings API allows mods to disable or enable Extra Settings API settings at runtime, keeping them visible but non-interactive. This page covers everything needed to integrate it into your own mod.

> **For hiding/showing settings:** Extra Settings API already supports this natively. Use [`CheckSettingVisibility`](https://github.com/Aidanamite/Extra-Settings-API/wiki/API-Calls#void-checksettingvisibility) and [`HandleSettingVisible`](https://github.com/Aidanamite/Extra-Settings-API/wiki/API-Events#handlesettingvisiblestringbool) from Extra Settings API to hide or show settings at runtime. This mod only handles the disable/enable (grayed-out) use case.

## Table of Contents

1. [Integration](#integration)
2. [Identifying Settings](#identifying-settings)
3. [API Reference](#api-reference)
4. [Examples](#examples)
5. [Troubleshooting](#troubleshooting)

---

## Integration

Runtime Settings API does not require a hard dependency in your `modinfo.json`. Instead, copy `RuntimeSettingsAPIHelper.cs` from this repository into your mod project.

The helper class handles everything:
- Scanning loaded assemblies to locate Runtime Settings API
- Waiting silently until it is available
- Queueing call if the target mod's settings have not loaded yet
- Timing out gracefully if Runtime Settings API is not installed

Once you have copied the file, call the static methods on `RuntimeSettingsAPIHelper` from anywhere in your mod.

### Example: Copy into your project

Place `RuntimeSettingsAPIHelper.cs` alongside your main mod file. No namespace or reference changes are needed  it compiles as part of your mod.

---

## Identifying Settings

Every API call takes two string parameters: `modName` and `settingName` (or `sectionName`).

### modName

This is the `name` field from the target mod's `modinfo.json`. It is case-sensitive and must match exactly.

Example `modinfo.json`:
```json
{
  "name": "HitMarker"
}
```

Call:
```csharp
RuntimeSettingsAPIHelper.HideSection(this, "HitMarker", "comboSection");
```

### settingName / sectionName

This is the `name` field of the individual setting or section in the target mod's `modinfo.json`. It is case-sensitive.

Example setting definition:
```json
{
  "type": "Section",
  "name": "comboSection",
  "text": "Combo Counter"
}
```

---

## API Reference

All methods are static and available on `RuntimeSettingsAPIHelper`. The first parameter is always `this` (your mod instance), used to start the background coroutine.

---

### DisableSetting

Disables a setting. The setting remains visible but is grayed out and non-interactive. The exact visual effect depends on the setting type.

```csharp
RuntimeSettingsAPIHelper.DisableSetting(Mod mod, string modName, string settingName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.DisableSetting(this, "MyMod", "worldGenSeed");
```

---

### EnableSetting

Enables a previously disabled setting.

```csharp
RuntimeSettingsAPIHelper.EnableSetting(Mod mod, string modName, string settingName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.EnableSetting(this, "MyMod", "worldGenSeed");
```

---

### timeout parameter

All methods accept an optional `timeout` float (in seconds, default 30). If Runtime Settings API is not available within the timeout period, the operation is silently dropped. Increase this if your mod loads late.

---

## Examples

### Disable a setting during gameplay

```csharp
void ExtraSettingsAPI_WorldLoad()
{
    RuntimeSettingsAPIHelper.DisableSetting(this, "MyMod", "worldGenSeed");
}

void ExtraSettingsAPI_WorldUnload()
{
    RuntimeSettingsAPIHelper.EnableSetting(this, "MyMod", "worldGenSeed");
}
```

---

## Troubleshooting

### Setting is not being disabled

- Confirm the `modName` matches the `name` field in the target mod's `modinfo.json` exactly (case-sensitive).
- Confirm the `settingName` matches the setting's `name` field exactly.
- Make sure Runtime Settings API is installed and loaded before the call runs. The helper waits up to 30 seconds by default.

### Nothing happens and there are no errors

- The helper operates silently. If Runtime Settings API is not installed, operations are dropped after the timeout with no console output by design.
- To verify Runtime Settings API is active, open the Mod Manager and confirm it appears in the loaded mods list.