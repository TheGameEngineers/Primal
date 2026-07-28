
# Primal Engine

<img align="left" alt="Primal watermark" src="https://github.com/Rashmatash/ImageRepo/blob/master/watermark.png?raw=true" />

<br/><br/>
<br/><br/>
<br/><br/>
<br/>

[![Discord chat](https://img.shields.io/discord/740606294846865549?logo=discord)](https://discord.gg/75ZmXwz)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![YouTube Channel Subscribers](https://img.shields.io/youtube/channel/subscribers/UCU0ZLgIv87jlqS6G58DKPyg)](https://www.youtube.com/gameengineseries?sub_confirmation=1)
[![X (formerly Twitter) Follow](https://img.shields.io/twitter/follow/primalnippleman)](https://img.shields.io/twitter/follow/primalnippleman.svg?style=social)

## Overview

Primal Engine is an open-source, in-development 3D game engine and editor. The core engine is a native C++ static library and renderer (Direct3D 12). A managed WPF-based editor (PrimalEditor) is provided as a .NET front-end that connects to the native engine via an Engine DLL for editor-enabled builds. The repository also includes native tooling (ContentTools) for asset import and processing.

The project's development history is documented on the [Game Engine Series](https://www.youtube.com/gameengineseries) YouTube channel, which explains nearly every line of code committed up to June 15, 2026.

## Key features

- Native C++ engine and renderer using Direct3D 12 (DXC shader toolchain)
- Managed WPF editor (PrimalEditor) written in C#/.NET
- Content import and processing (3D: FBX, 2D: JPEG/PNG/TGA/TIFF/BMP/DDS/HDR)
- Engine DLL exposes a native API to the editor
- Example/test native application (EngineTest)

## Requirements

- Windows 10 or later
- Visual Studio 2026 (x64 workloads: C++ and .NET/WPF)
- DirectX 12 capable GPU (Shader Model 6.6 or later)

## Dependencies

- [FBX SDK 2020](https://www.autodesk.com/developer-network/platform-technologies/fbx-sdk-2020-0) (manual install). Example expected include path: `C:\Program Files\Autodesk\FBX\FBX SDK\2020.3.7`
- DirectXTex (automatically installed via vcpkg in ContentTools)
- DXC / D3D12 Agility SDK (automatically installed via NuGet packages in EngineDLL/Engine/EngineTest)
- EnvDTE and EnvDTE80 for Visual Studio automation (automatically installed via NuGet in PrimalEditor)

## Repository layout (high level)

- Engine/         — Core native static library (renderer, platform, input, components)
- EngineDLL/      — Engine DLL wrapper exposing a native API to the editor
- ContentTools/   — Native tools for importing and processing FBX files and textures
- EngineTest/     — Native test application demonstrating renderer features
- PrimalEditor/   — WPF (C#/.NET) editor and tooling front-end


## Quick start

1. Clone the repository: `git clone https://github.com/TheGameEngineers/Primal.git`
2. Open Primal.slnx in Visual Studio 2026.
3. Restore NuGet packages and vcpkg ports (Visual Studio will normally restore automatically).
4. Install the FBX SDK and update the include/lib paths in ContentTools.vcxproj and FbxImporter.cpp if your install location differs from the example above.
5. Select a solution configuration:
   - DebugEditor / ReleaseEditor — editor-enabled builds
   - Debug / Release — runtime/test builds
6. Build the solution (x64). Building DebugEditor/ReleaseEditor will also produce the PrimalEditor output under the configured OutputPath (e.g. ..\x64\DebugEditor\).

## Running the editor

1. Build using DebugEditor or ReleaseEditor configuration.
2. Run PrimalEditor from the build output directory. The project files will copy Resources/ProjectTemplates and Resources/DefaultAssets into the output as needed.

## Running EngineTest

<img align="right" src="https://github.com/Rashmatash/ImageRepo/blob/master/Animation.gif?raw=true" width="480px"/>

EngineTest is a native application that demonstrates renderer and content pipeline behavior. Before running, ensure the sample content (meshes and textures) required by EngineTest is imported and available in the engine content path.

1. Use PrimalEditor to import sample assets via the Content Browser. The editor includes helper code that can temporarily pack imported assets for EngineTest (see the notes below).

2. If necessary, enable the export/pack code in the editor source to write model and texture files to the expected EngineTest locations:

   - PrimalEditor/Content/Geometry.cs — PackForEngine() may write ..\..\x64\model.model
   - PrimalEditor/Content/Texture.cs  — PackForEngine() may write ..\..\x64\texture.img

   These sections are guarded/commented for safety; they were added for short-term testing. Revert any changes after exporting the required assets.

3. Rename the model and texture files to match the expected names in EngineTest's RenderItem.cpp.
 
4. Build the solution and the EngineTest project (x64, Debug or Release).

5. Run the EngineTest.

## Reporting issues

Please open issues for bugs or feature requests on the GitHub Issues page.

## License

This repository is licensed under the MIT License. See the LICENSE file in the repository root for full terms.

## Troubleshooting & help

- Join the [Discord](https://discord.gg/75ZmXwz) server for your questions or requesting help.
- Report bugs in [GitHub Issues](https://github.com/TheGameEngineers/Primal/issues).
- Watch the [Game Engine Series](https://www.youtube.com/gameengineseries) YouTube channel for development history and explanations of the code.
