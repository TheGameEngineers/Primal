
# Primal Engine

<img align="left" alt="Primal watermark" src="https://github.com/Rashmatash/ImageRepo/blob/master/watermark.png?raw=true" />

<br/><br/>
<br/><br/>
<br/><br/>
<br/>

![Github top languages](https://img.shields.io/github/languages/top/TheGameEngineers/Primal)
[![GitHub issues](https://img.shields.io/github/issues/TheGameEngineers/Primal?style=flat-square)](https://github.com/TheGameEngineers/Primal/issues)
[![GitHub stars](https://img.shields.io/github/stars/TheGameEngineers/Primal?style=flat-square)](https://github.com/TheGameEngineers/Primal/stargazers)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Discord chat](https://img.shields.io/discord/740606294846865549?logo=discord)](https://discord.gg/75ZmXwz)
[![YouTube Channel Subscribers](https://img.shields.io/youtube/channel/subscribers/UCU0ZLgIv87jlqS6G58DKPyg)](https://www.youtube.com/gameengineseries?sub_confirmation=1)
[![X (formerly Twitter) Follow](https://img.shields.io/twitter/follow/primalnippleman)](https://img.shields.io/twitter/follow/primalnippleman.svg?style=social)

### Overview

**Primal Engine** is an open-source 3D game engine and editor currently under active development. The core engine is implemented as a native C++ static library. Its level editor, **PrimalEditor**,
is a managed WPF-based C#/.NET application that communicates with the native engine through an Engine DLL in editor-enabled builds. The repository also provides native tooling (**ContentTools**) for asset
import and processing.

The project’s full development history is documented on the [Game Engine Series](https://www.youtube.com/gameengineseries) YouTube channel, which walks through nearly every line of code committed
through June 15, 2026.

### Key features

- Native C++ engine and renderer built on Direct3D 12 (using the DXC shader toolchain)
- Managed WPF editor (PrimalEditor) written in C# / .NET
- Content import and processing pipeline supporting 3D assets (FBX) and 2D textures (JPEG, PNG, TGA, TIFF, BMP, DDS, HDR)
- Engine DLL that exposes a clean native API to the editor
- Example/test native application (EngineTest)


### Requirements

- Windows 10 or later
- Visual Studio 2026 with the x64 C++ and .NET/WPF workloads
- A DirectX 12-capable GPU supporting Shader Model 6.6 or higher

### Dependencies

- [FBX SDK 2020](https://www.autodesk.com/developer-network/platform-technologies/fbx-sdk-2020-0) (manual installation required). A typical install path looks like:
 `C:\Program Files\Autodesk\FBX\FBX SDK\2020.3.7`
- MikkTSpace (already included with ContentTools)
- DirectXTex (automatically restored via vcpkg for ContentTools)
- DXC and the D3D12 Agility SDK (automatically restored via NuGet packages for EngineDLL, Engine, and EngineTest)
- EnvDTE and EnvDTE80 for Visual Studio automation (automatically restored via NuGet for PrimalEditor)

### Repository layout (high level)

- `Engine/` — Core native static library (renderer, platform layer, input, components)
- `EngineDLL/` — Thin DLL wrapper that exposes the native engine API to the editor
- `ContentTools/` — Native tools for importing and processing FBX models and textures
- `EngineTest/` — Stand-alone native application used to exercise and validate engine features
- `PrimalEditor/` — WPF (C# / .NET) editor and tooling front-end

### Quick start

1. Clone the repository: `git clone https://github.com/TheGameEngineers/Primal.git`
2. Open `Primal.slnx` in Visual Studio 2026.
3. Restore NuGet packages and vcpkg ports (Visual Studio normally does this automatically).
4. Install the FBX SDK and, if necessary, update the include/library paths in `ContentTools.vcxproj` and `FbxImporter.cpp` to match your installation location.
5. Choose a solution configuration:
   - **DebugEditor / ReleaseEditor** — builds with editor support
   - **Debug / Release** — pure runtime / test builds
6. Build the solution for the x64 platform. Building a DebugEditor or ReleaseEditor configuration also produces the PrimalEditor binaries under the configured
   OutputPath (for example `\x64\DebugEditor`).

### Running the editor

1. Build using the DebugEditor or ReleaseEditor configuration.
2. Set `PrimalEditor` as the startup project and launch it from Visual Studio (press F5), or run `PrimalEditor.exe` from the resulting build-output directory.

### Running EngineTest

<img align="right" src="https://github.com/Rashmatash/ImageRepo/blob/master/Animation.gif?raw=true" width="480px"/>

EngineTest is a minimal game that demonstrates and validates the engine’s capabilities.
1. Build with the Debug or Release configuration.
   - **Note:** On the first build of the EngineTest project, MSBuild automatically downloads and unpacks a zip file into the `\Primal\x64\` folder. These files contain the binary assets
	 (models and textures) required by the test scene.
2. Set `EngineTest` as the startup project and run it from Visual Studio (press F5), or run `EngineTest.exe` from the build-output directory.
   - Use **WASD** to move the camera and the mouse (while holding the left button) to look around.
   - **Alt-Enter** toggles fullscreen mode.
   - **Esc** exits the application.

### Reporting issues

Please file bugs on the project’s [GitHub Issues](https://github.com/TheGameEngineers/Primal/issues) page.

### License

This repository is released under the [MIT License](LICENSE).<br/>
All third-party licenses are included in the `Licenses` folder of the repository.

### Troubleshooting & help

- Join the [Discord](https://discord.gg/75ZmXwz) server for questions and community support.
- Report bugs via [GitHub Issues](https://github.com/TheGameEngineers/Primal/issues).
- Watch the [Game Engine Series](https://www.youtube.com/gameengineseries) YouTube channel for a complete development history and detailed code walkthroughs (through June 15, 2026).