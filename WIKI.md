# Runtime Settings API - Wiki

Runtime Settings API allows mods to hide, show, enable, or disable Extra Settings API settings at runtime. This page covers everything needed to integrate it into your own mod.

## Table of Contents

1. [Integration](#integration)
2. [Identifying Settings](#identifying-settings)
3. [API Reference](#api-reference)
4. [Queueing](#queueing)
5. [Examples](#examples)
6. [Troubleshooting](#troubleshooting)

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

### HideSetting

Hides a single setting. The setting's GameObject is deactivated and will not appear in the settings panel.

```csharp
RuntimeSettingsAPIHelper.HideSetting(Mod mod, string modName, string settingName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.HideSetting(this, "HitMarker", "comboResetTime");
```

---

### ShowSetting

Shows a previously hidden setting. The setting is only shown if Extra Settings API considers it eligible to be shown (respects world/non-world context).

```csharp
RuntimeSettingsAPIHelper.ShowSetting(Mod mod, string modName, string settingName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.ShowSetting(this, "HitMarker", "comboResetTime");
```

---

### HideSection

Hides a section header and all settings that belong to that section. Child settings are hidden individually so they can be shown again independently if needed.

```csharp
RuntimeSettingsAPIHelper.HideSection(Mod mod, string modName, string sectionName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.HideSection(this, "HitMarker", "comboSection");
```

---

### ShowSection

Shows a previously hidden section and all its child settings.

```csharp
RuntimeSettingsAPIHelper.ShowSection(Mod mod, string modName, string sectionName, float timeout = 30f)
```

Example:
```csharp
RuntimeSettingsAPIHelper.ShowSection(this, "HitMarker", "comboSection");
```

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

## Queueing

If you call a hide or show method before the target mod's settings have been loaded into Extra Settings API, the operation is automatically queued. It will be applied the next time Extra Settings API loads settings for that mod.

This means it is safe to call API methods from `Start()`, `ExtraSettingsAPI_Load()`, or any other early lifecycle point. You do not need to wait.

Operations that are queued:
- `HideSetting`
- `HideSection`

Operations that are not queued (the target mod's settings must exist):
- `ShowSetting`
- `ShowSection`
- `DisableSetting`
- `EnableSetting`

Show and enable operations are not queued because they are only meaningful after a previous hide or disable has been applied.

---

## Examples

### Hide a section when another mod is loaded

This is the primary use case. If your mod has built-in functionality that is superseded by a dedicated mod, hide the relevant settings.

```csharp
void ExtraSettingsAPI_Load()
{
    if (IsComboCounterModLoaded())
        RuntimeSettingsAPIHelper.HideSection(this, "HitMarker", "comboSection");
}

private bool IsComboCounterModLoaded()
{
    foreach (var mod in ModManagerPage.modList)
    {
        var modInfo = mod.jsonmodinfo;
        if (modInfo.name == "ComboCounter" && mod.modState == ModInfo.ModStateEnum.running)
            return true;
    }
    return false;
}
```

### Show advanced settings only when enabled

```csharp
void ExtraSettingsAPI_SettingsChanged(string settingName)
{
    if (settingName == "enableAdvanced")
    {
        bool enabled = ExtraSettingsAPI_GetCheckboxState("enableAdvanced");
        if (enabled)
            RuntimeSettingsAPIHelper.ShowSection(this, "MyMod", "advancedSection");
        else
            RuntimeSettingsAPIHelper.HideSection(this, "MyMod", "advancedSection");
    }
}
```

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

### React to another mod loading at runtime

Runtime Settings API supports detecting mods that are loaded or unloaded after your mod has already started. Poll periodically in `Update()`:

```csharp
private float checkTimer = 0f;
private bool otherModDetected = false;

public void Update()
{
    checkTimer += Time.deltaTime;
    if (checkTimer < 2f) return;
    checkTimer = 0f;

    bool nowLoaded = IsOtherModLoaded();
    if (nowLoaded && !otherModDetected)
    {
        otherModDetected = true;
        RuntimeSettingsAPIHelper.HideSection(this, "MyMod", "redundantSection");
    }
    else if (!nowLoaded && otherModDetected)
    {
        otherModDetected = false;
        RuntimeSettingsAPIHelper.ShowSection(this, "MyMod", "redundantSection");
    }
}
```

---

## Troubleshooting

### Settings are not hiding

- Confirm the `modName` matches the `name` field in the target mod's `modinfo.json` exactly (case-sensitive).
- Confirm the `settingName` matches the setting's `name` field exactly.
- Make sure Runtime Settings API is installed and loaded before the call runs. The helper waits up to 30 seconds by default.
- If you are calling from `Start()` and the target mod loads after yours, the operation will be queued for hide/section operations. For show/enable operations, call from `ExtraSettingsAPI_Load()` instead.

### Settings come back after opening/closing the settings panel

This is a known limitation of Extra Settings API: reopening the panel calls `ToggleSettings`, which re-evaluates visibility. Runtime Settings API patches this method, so the hidden state should persist. If it does not:

- Confirm Runtime Settings API is loaded (check the in-game mod list).
- Check the console for any error messages from RuntimeSettingsAPI.

### Nothing happens and there are no errors

- The helper operates silently. If Runtime Settings API is not installed, operations are dropped after the timeout with no console output by design.
- To verify Runtime Settings API is active, open the Mod Manager and confirm it appears in the loaded mods list.