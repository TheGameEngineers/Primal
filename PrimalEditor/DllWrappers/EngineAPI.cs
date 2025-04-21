// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.EngineAPIStructs;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace PrimalEditor.EngineAPIStructs
{
    [StructLayout(LayoutKind.Sequential)]
    class TransformComponent
    {
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale = new(1, 1, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    class ScriptComponent
    {
        public IntPtr ScriptCreator;
    }

    [StructLayout(LayoutKind.Sequential)]
    class GameEntityDescriptor
    {
        public TransformComponent Transform = new();
        public ScriptComponent Script = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    class ShaderData : IDisposable
    {
        public int Type;
        public int CodeSize;
        public int ByteCodeSize;
        public int ErrorsSize;
        public int AssemblySize;
        public int HashSize;
        public IntPtr Code;
        public IntPtr ByteCodeErrorAssemblyHash;
        public string FunctionName;
        public string ExtraArgs;
        public void Dispose()
        {
            Marshal.FreeCoTaskMem(ByteCodeErrorAssemblyHash);
            Marshal.FreeCoTaskMem(Code);
            GC.SuppressFinalize(this);
        }

        ~ShaderData()
        {
            Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class ShaderGroupData : IDisposable
    {
        public int Type;
        public int Count;
        public int DataSize;
        public IntPtr Data;
        public void Dispose()
        {
            Marshal.FreeCoTaskMem(Data);
            GC.SuppressFinalize(this);
        }

        ~ShaderGroupData()
        {
            Dispose();
        }
    }
}

namespace PrimalEditor.DllWrappers
{
    static class EngineAPI
    {
        private const string _engineDll = "EngineDll.dll";
        [DllImport(_engineDll, CharSet = CharSet.Ansi)]
        public static extern int LoadGameCodeDll(string dllPath);
        [DllImport(_engineDll)]
        public static extern int UnloadGameCodeDll();
        [DllImport(_engineDll)]
        public static extern IntPtr GetScriptCreator(string name);
        [DllImport(_engineDll)]
        [return: MarshalAs(UnmanagedType.SafeArray)]
        public static extern string[] GetScriptNames();
        [DllImport(_engineDll)]
        public static extern IdType CreateRenderSurface(IntPtr host, int width, int height);
        [DllImport(_engineDll)]
        public static extern void RemoveRenderSurface(IdType surfaceId);
        [DllImport(_engineDll)]
        public static extern void ResizeRenderSurface(IdType surfaceId);
        [DllImport(_engineDll)]
        public static extern IntPtr GetWindowHandle(IdType surfaceId);
        [DllImport(_engineDll)]
        private static extern IdType CreateResource(IntPtr data, int type);

        public static IdType CreateResource(byte[] resourceData, AssetType type)
        {
            throw new NotImplementedException();
        }

        [DllImport(_engineDll)]
        public static extern void DestroyResource(IdType id, int type);

        [DllImport(_engineDll)]
        private static extern IdType AddShaderGroup([In] ShaderGroupData data);

        public static IdType AddShaderGroup(ShaderGroup shaderGroup)
        {
            using var data = new ShaderGroupData();
            data.Type = (int)shaderGroup.Type;
            data.Count = shaderGroup.Count;

            var packedData = shaderGroup.PackForEngine();

            if (packedData == null || packedData.Length == 0)
            {
                throw new Exception("Invalid shader data.");
            }

            data.DataSize = packedData.Length;
            data.Data = Marshal.AllocCoTaskMem(data.DataSize);

            Marshal.Copy(packedData, 0, data.Data, data.DataSize);
            return AddShaderGroup(data);
        }

        [DllImport(_engineDll)]
        public static extern void RemoveShaderGroup(IdType id);

        [DllImport(_engineDll)]
        private static extern int CompileShader([In, Out] ShaderData data);

        public static void CompileShader(ShaderGroup shaderGroup)
        {
            Debug.Assert(!string.IsNullOrEmpty(shaderGroup?.Code));
            Debug.Assert(!string.IsNullOrEmpty(shaderGroup.FunctionName));
            Debug.Assert(shaderGroup.ExtraArgs?.Any() == true);
            Debug.Assert(!shaderGroup.ByteCode.Any() == true);
            shaderGroup.ByteCode.Clear();
            shaderGroup.Errors.Clear();
            shaderGroup.Assembly.Clear();

            try
            {
                foreach (var args in shaderGroup.ExtraArgs)
                {
                    using var data = new ShaderData();
                    var code = Encoding.Default.GetBytes([.. shaderGroup.Code]);
                    data.Type = (int)shaderGroup.Type;
                    data.CodeSize = code.Length;
                    data.FunctionName = shaderGroup.FunctionName;
                    data.ExtraArgs = args.Any() ? string.Join(";", args) : string.Empty;
                    data.Code = Marshal.AllocCoTaskMem(code.Length);
                    Marshal.Copy(code, 0, data.Code, data.CodeSize);
                    if (CompileShader(data) == 0) throw new Exception("Shader compilation failed.");

                    var bytes = new byte[data.ByteCodeSize + data.ErrorsSize + data.AssemblySize + data.HashSize];
                    Marshal.Copy(data.ByteCodeErrorAssemblyHash, bytes, 0, bytes.Length);

                    int offset = 0;

                    if (data.ByteCodeSize > 0)
                    {
                        var byteCode = new byte[data.ByteCodeSize];
                        Array.Copy(bytes, offset, byteCode, 0, data.ByteCodeSize);
                        shaderGroup.ByteCode.Add(byteCode);
                        offset += data.ByteCodeSize;
                    }
                    else
                    {
                        shaderGroup.ByteCode.Add([]);
                    }

                    if (data.ErrorsSize > 0)
                    {
                        var errors = new byte[data.ErrorsSize];
                        Array.Copy(bytes, offset, errors, 0, data.ErrorsSize);
                        var errorString = Encoding.Default.GetString(errors);
                        shaderGroup.Errors.Add(errorString);
                        Logger.Log(data.ByteCodeSize > 0 ? MessageType.Warning : MessageType.Error, errorString);
                        offset += data.ErrorsSize;
                    }
                    else
                    {
                        shaderGroup.Errors.Add(string.Empty);
                    }

                    if (data.AssemblySize > 0)
                    {
                        var assembly = new byte[data.AssemblySize];
                        Array.Copy(bytes, offset, assembly, 0, data.AssemblySize);
                        shaderGroup.Assembly.Add(Encoding.Default.GetString(assembly));
                        offset += data.AssemblySize;
                    }
                    else
                    {
                        shaderGroup.Assembly.Add(string.Empty);
                    }

                    if (data.HashSize > 0)
                    {
                        var hash = new byte[data.HashSize];
                        Array.Copy(bytes, offset, hash, 0, data.HashSize);
                        shaderGroup.Hash.Add(hash);
                        offset += data.HashSize;
                    }
                    else
                    {
                        shaderGroup.Hash.Add([]);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to compile shader {shaderGroup.FunctionName}");
                Debug.WriteLine(ex.Message);
                throw;
            }
        }

        internal static class EntityAPI
        {
            [DllImport(_engineDll)]
            private static extern IdType CreateGameEntity(GameEntityDescriptor desc);
            public static IdType CreateGameEntity(GameEntity entity)
            {
                GameEntityDescriptor desc = new();

                //transform component
                {
                    var c = entity.GetComponent<Transform>();
                    desc.Transform.Position = c.Position;
                    desc.Transform.Rotation = c.Rotation;
                    desc.Transform.Scale = c.Scale;
                }
                // script component
                {
                    // NOTE: here we also check if current project is not null, so we can tell whether the game code DLL
                    //       has been loaded or not. This way, creation of entities with a script component is deferred
                    //       until the DLL has been loaded.
                    var c = entity.GetComponent<Script>();
                    if(c != null && Project.Current !=null)
                    {
                        if(Project.Current.AvailableScripts.Contains(c.Name))
                        {
                            desc.Script.ScriptCreator = GetScriptCreator(c.Name);
                        }
                        else
                        {
                            Logger.Log(MessageType.Error, $"Unable to find script with name {c.Name}. Game entity will be created without script component!");
                        }
                    }
                }
                return CreateGameEntity(desc);
            }

            [DllImport(_engineDll)]
            private static extern void RemoveGameEntity(IdType id);
            public static void RemoveGameEntity(GameEntity entity)
            {
                RemoveGameEntity(entity.EntityId);
            }
        }
    }
}
