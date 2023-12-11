# GLEBuildTool
The Build tool for the Galaxy Level Engine.

## Requirements

- `Galaxy Level Engine` from the [Galaxy Level Engine Github](https://github.com/SuperHackio/GalaxyLevelEngine)
- `powerpc-eabi-as.exe` from DevKitPro.
- `.NET 8.0` from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

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

where `<Region>` can be `NTSC-U`, `PAL`, `NTSC-J`, `NTSC-K`, or `NTSC-W` (for North America, Europe, Japan, Korea, and Taiwan respectively)<br/>Keep in mind that not all GLE versions support all of these regions.
