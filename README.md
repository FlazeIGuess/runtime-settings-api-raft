# Runtime Settings API for Raft

A mod that extends Extra Settings API with the ability to disable or enable settings at runtime without restarting the game or reloading the mod.

> **Note on hiding/showing settings:** Extra Settings API already supports toggling setting visibility while the settings menu is open via [`CheckSettingVisibility`](https://github.com/Aidanamite/Extra-Settings-API/wiki/API-Calls#void-checksettingvisibility) and [`HandleSettingVisible`](https://github.com/Aidanamite/Extra-Settings-API/wiki/API-Events#handlesettingvisiblestringbool). Use those built-in features for visibility control. This mod focuses solely on the ability to keep settings visible but non-interactive (disabled/grayed out).

## Features

- Disable or enable settings (visible but non-interactive/grayed out)
- Query the current enabled state of any setting
- Works with all Extra Settings API setting types
- Client-side only (does not require all players to have it installed)

## Installation

### For Players

1. Install [RaftModLoader](https://www.raftmodding.com/loader)
2. Install [Extra Settings API](https://www.raftmodding.com/mods/extra-settings-api)
3. Download the latest `RuntimeSettingsAPI.rmod` from the [releases page](https://github.com/FlazeIGuess/runtime-settings-api-raft/releases)
4. Place the `.rmod` file in your RaftModLoader mods folder
5. Launch Raft through RaftModLoader

### For Mod Developers

You do not need to add Runtime Settings API as a hard dependency. Instead, copy `RuntimeSettingsAPIHelper.cs` into your mod project. The helper detects Runtime Settings API at runtime and handles all communication automatically.

See [WIKI.md](WIKI.md) for full integration instructions and API reference.

## Configuration

Runtime Settings API has no user-facing settings of its own. It operates silently in the background and is only relevant to other mod developers.

## How It Works

Runtime Settings API uses reflection to locate Extra Settings API after it has loaded. Once found, it provides a simple interface for disabling settings, making them visible but non-interactive.

Mods interact with Runtime Settings API through `RuntimeSettingsAPIHelper.cs`, a self-contained helper class that:

1. Scans loaded assemblies for the Runtime Settings API mod at runtime
2. Polls silently until it is available (up to a configurable timeout)
3. Calls the appropriate API method once ready

## Building from Source

### Prerequisites

- Visual Studio 2019 or later
- .NET Framework 4.8
- Raft game installed
- RaftModLoader installed

### Build Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/FlazeIGuess/runtime-settings-api-raft.git
   cd runtime-settings-api-raft
   ```

2. Update the reference paths in `RuntimeSettingsAPI/RuntimeSettingsAPI.csproj` to match your Raft installation directory

3. Build the solution:
   ```bash
   msbuild RuntimeSettingsAPI.sln /p:Configuration=Debug
   ```

   Or open `RuntimeSettingsAPI.sln` in Visual Studio and build from there

4. The mod will be automatically packaged as `RuntimeSettingsAPI.rmod` in the root directory

## Contributing

Contributions are welcome. Please fork the repository, create a branch for your change, and open a pull request with a clear description of what was changed and why.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Extra Settings API by [Aidanamite](https://github.com/Aidanamite/Extra-Settings-API)
- Built with [RaftModLoader](https://www.raftmodding.com/)
- Uses [Harmony](https://github.com/pardeike/Harmony) for runtime patching
- Created by Flaze

## Support

- Report bugs on the [Issues page](https://github.com/FlazeIGuess/runtime-settings-api-raft/issues)
- Join the [Raft Modding Discord](https://www.raftmodding.com/discord)