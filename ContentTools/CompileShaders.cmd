@echo off
rem Copyright (c) Arash Khatami
rem Distributed under the MIT license. See the LICENSE file in the project root for more information.
rem Adapted from DirectXTex library (see https://github.com/microsoft/DirectXTex/blob/main/DirectXTex/Shaders/CompileShaders.cmd)
setlocal
set error=0

set FXOPTS=/nologo /Tcs_5_0 /WX /Ges /Zi /Zpc /Qstrip_reflect /Qstrip_debug

set PCFXC="%WindowsSDKExecutablePathX64%\fxc.exe"
if exist %PCFXC% goto continue

set PCFXC=fxc.exe

:continue
if not defined CompileShadersOutput set CompileShadersOutput=x64\Compiled
@if not exist %CompileShadersOutput% mkdir "%CompileShadersOutput%"
echo.
call :CompileShader EnvMapProcessing EquirectangularToCubeMapCS
call :CompileShader EnvMapProcessing PrefilterDiffuseEnvMapCS
call :CompileShader EnvMapProcessing PrefilterSpecularEnvMapCS

endlocal
exit /b 0

:CompileShader
set fxc=%PCFXC% "%1.hlsl" %FXOPTS% /E%2 "/Fh%CompileShadersOutput%\%1_%2.inc" "/Fd%CompileShadersOutput%\%1_%2.pdb" /Vn%1_%2
echo compiling %1_%2
if not exist %CompileShadersOutput%\%1_%2.inc %fxc% || set error=1
echo.
exit /b