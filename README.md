<img align=left src="https://github.com/Rashmatash/ImageRepo/blob/master/watermark.png?raw=true" />

# Primal Engine

[![Discord chat](https://img.shields.io/discord/740606294846865549?logo=discord)](https://discord.gg/75ZmXwz)
![X (formerly Twitter) Follow](https://img.shields.io/twitter/follow/primalnippleman)
![YouTube Channel Subscribers](https://img.shields.io/youtube/channel/subscribers/UCU0ZLgIv87jlqS6G58DKPyg)


## Description
Primal Engine is a cross-component 3D game engine and editor in active development since 2020. The engine core is a native C++ renderer and runtime that targets Direct3D12 and modern HLSL/Shader Model pipelines. The repository also contains native tooling (ContentTools) for importing and processing assets (FBX, textures, primitive meshes, normal-map detection, mip/texture work) and a managed WPF-based editor (PrimalEditor) written in C# targeting .NET 10.

Key features
- Native C++ engine and renderer using Direct3D12 (D3D12/DXC shader toolchain).
- Content import and processing tools (FBX importer, texture and primitive mesh utilities).
- WPF-based editor and project templates that integrate with the native engine via an Engine DLL build used when running editor configurations.
- Example/test application (EngineTest) for running renderer and scene tests.

## Requirements and dependencies

- Visual Studio 2022 or later (x64 workloads for C++ and .NET/WPF)
- Windows 10 or later
- DirectX 12 capable GPU (shader model support required by your target shaders)
- FBX SDK 2020 (project references expect FBX SDK headers at e.g. C:\Program Files\Autodesk\FBX\FBX SDK\2020.3.7\include) for ContentTools
- DirectXTex (provided via NuGet in ContentTools/Engine projects)
- DXC/D3D12 NuGet packages (used by EngineDLL/Engine/EngineTest projects)
- .NET 10 (PrimalEditor targets net10.0-windows7.0)

Note: several native projects use custom MSBuild targets that require NuGet package restore and a working Windows SDK toolchain (FXC/DXC paths) for shader compilation.

## How to build
1. Open Primal.slnx in Visual Studio (ensure x64 platform is selected).
2. Restore NuGet packages (Visual Studio does this automatically on solution load or use "Restore NuGet Packages").
3. Install the FBX SDK (if you plan to build ContentTools) and ensure the include path in ContentTools.vcxproj matches your FBX install location.
4. Select the appropriate solution configuration:
   - DebugEditor / ReleaseEditor for editor-enabled builds (engine built with USE_WITH_EDITOR, editor executable output placed in ..\x64\<Configuration>\)
   - Debug / Release for runtime/test builds
5. Build the solution for all configurations (Debug, DebugEditor, Release, ReleaseEditor) to ensure all projects are built.

## Running the editor
1. Build the solution using the DebugEditor or ReleaseEditor configuration.
2. Launch PrimalEditor (PrimalEditor\bin or the configured OutputPath e.g. ..\x64\DebugEditor\). The editor includes Resources/ProjectTemplates and Resources/DefaultAssets copied to the output directory by the project file.
3. Use the Content Browser and built-in editors to import assets and create projects. The editor depends on the EngineDLL and native Engine artifacts when running editor-enabled builds.

## Running EngineTest project
EngineTest is a native test application that exercises the renderer, lighting and content pipeline. Before running EngineTest you must ensure the test content (meshes, textures, materials) used by the sample scenes is imported into the project's content folder.

1. Build the solution and the EngineTest project with the Debug or Release configuration (x64).
2. Prepare the content used by EngineTest:
   - Launch PrimalEditor (use DebugEditor/ReleaseEditor build) and open or create a project.
   - Use the Content Browser to import the sample meshes and textures required by the tests. The editor provides import settings for geometry and textures.
   - If you prefer to enable the editor's built-in sample import helpers, open the following files and enable the relevant code sections (they are intentionally guarded or commented out in the editor source):
     - PrimalEditor/Content/Geometry.cs
     - PrimalEditor/Content/Texture.cs
   - After importing, verify that assets appear in your project's content folder (DefaultAssets or your project's asset folder copied to the EngineTest content path).
3. Run the EngineTest executable (located in the project's output directory). The test runner will load content from the engine's expected content locations and run renderer/scene tests.

If EngineTest fails to find assets at runtime, re-open the editor and confirm the imported assets are present and that the project's content destination matches the EngineTest content path. Also ensure NuGet packages are restored and the Windows SDK (with FXC/DXC) is installed if shader compilation is required.

Temporary editor test packaging
- For debugging convenience the editor source currently enables temporary packaging calls for imported assets. Specifically:
  - PrimalEditor/Content/Geometry.cs: PackForEngine() is invoked in Load() and the packed geometry is written to ..\\..\\x64\\model.model
  - PrimalEditor/Content/Texture.cs: PackForEngine() is invoked in Load() and the packed texture is written to ..\\..\\x64\\texture.img
- These changes are intended for short-term testing only. Remove or revert these edits before committing or shipping the editor.
- Safer alternatives you can apply now or later:
  - Change the test output to a temp location (Path.GetTempPath()) or an explicit test-output folder inside your user or build artifacts directory.
  - Wrap the test writes with a compilation guard (e.g. #if DEBUG ... #endif) or gate them behind a runtime "write test packages" setting in the editor UI.
  - Revert the temporary changes by restoring the two files from source control: PrimalEditor/Content/Geometry.cs and PrimalEditor/Content/Texture.cs.

## Repo layout (high level)
- Engine/         -- Core native static library (renderer, platform, input, components)
- EngineDLL/      -- Engine DLL wrapper used by the editor (shader compilation helpers)
- ContentTools/   -- Native tools for importing and processing FBX, textures, and shaders
- EngineTest/     -- Native test application demonstrating renderer features
- PrimalEditor/   -- WPF (.NET 10) editor and tooling front-end

## License
This repository is licensed under the MIT License. See the LICENSE file in the repository root for full terms.
