# Runtime Settings API for Raft

## DEPRECATED - No longer needed

Extra Settings API's built-in `ExtraSettingsAPI_HandleSettingVisible` callback natively supports runtime visibility changes without a separate mod. **No new versions of Runtime Settings API will be released.**

**Players:** You can safely uninstall this mod.

**Developers:** Migrate to `ExtraSettingsAPI_HandleSettingVisible`. Set `"access": "GlobalCustom"` on any setting you want to control dynamically, then implement the callback in your mod class:

> ```csharp
> public bool ExtraSettingsAPI_HandleSettingVisible(string settingName, bool isInWorld)
> {
>     if (settingName == "mySection" || settingName == "mySetting")
>         return someCondition;
>     return true;
> }
> ```

ExtraSettingsAPI calls this automatically whenever the settings panel evaluates visibility - no Harmony patching, no helper file, no extra dependency required.

---

## What this mod was

Runtime Settings API was a standalone mod that extended Extra Settings API with the ability to hide, show, enable, or disable settings at runtime - without restarting the game or reloading the mod. It worked by using reflection to hook into ExtraSettingsAPI internals and applying a Harmony postfix to `ModSettingContainer.ToggleSettings` to persist hidden state across panel open/close cycles.

It turned out that ExtraSettingsAPI's own `HandleSettingVisible` event already covers the core visibility use case natively, making this mod unnecessary for the primary use case.

The one feature that has no ExtraSettingsAPI equivalent is **disabling settings** (visible but grayed out and non-interactive). If that is relevant to your mod, the source code remains available below.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Extra Settings API by [Aidanamite](https://github.com/Aidanamite/Extra-Settings-API)
- Built with [RaftModLoader](https://www.raftmodding.com/)
- Uses [Harmony](https://github.com/pardeike/Harmony) for runtime patching
- Created by Flaze
- Report bugs on the [Issues page](https://github.com/FlazeIGuess/runtime-settings-api-raft/issues)
- Join the [Raft Modding Discord](https://www.raftmodding.com/discord)
