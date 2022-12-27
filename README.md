# GLEBuildTool
The Build tool for the Galaxy Level Engine.

## Requirements

- `Galaxy Level Engine` from the [Galaxy Level Engine Github](https://github.com/SuperHackio/GalaxyLevelEngine)
- `powerpc-eabi-as.exe` from DevKitPro.
- `.NET 7.0` from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/7.0).

## Usage

Ensure the following file structure:

- GalaxyLevelEngine
  - Resources (Optional)
  - Source
  - Symbols
  - Tools
    - powerpc-eabi-as.exe
  - GLEBuildTool.deps.json
  - GLEBuildTool.dll
  - GLEBuildTool.exe
  - GLEBuildTool.pdb
  - GLEBuildTool.runtimeconfig.json
  
Once this structure is ready, open Command Prompt and run the following:

`GLEBuildTool.exe <Region>`

where `<Region>` can be `NTSC-U`, `PAL`, or `NTSC-J` (for North America, Europe, and Japan respectively)
