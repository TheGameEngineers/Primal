// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.EngineAPIStructs;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace PrimalEditor.EngineAPIStructs
{
    enum EngineInitError : int
    {
        [Description("Engine initialization succeeded")]
        Succeeded = 0,
        [Description("Unknown error occurred during engine initialization")]
        Unknown,
        [Description("Built-in shader compilation failed")]
        ShaderCompilation,
        [Description("Graphics module initialization failed")]
        Graphics,
    }

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
    class GeometryComponent : IDisposable
    {
        public IdType GeometryContentId = ID.INVALID_ID;
        public int MaterialCount;
        public IntPtr MaterialIds;

        public GeometryComponent() { }

        public GeometryComponent(Components.Geometry geometry)
        {
            GeometryContentId = geometry.ContentId;
            MaterialCount = geometry.MaterialsList.Count;
            Debug.Assert(MaterialCount == geometry.GeometryWithMaterials.LODs.Sum(x => x.Meshes.Count));

            byte[] data = null;
            using (var writer = new BinaryWriter(new MemoryStream()))
            {
                geometry.MaterialsList.ForEach(mtl => writer.Write(mtl.ContentId));
                writer.Flush();
                data = (writer.BaseStream as MemoryStream).ToArray();
            }

            Debug.Assert(data?.Length == geometry.MaterialsList.Count * sizeof(IdType));
            MaterialIds = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, MaterialIds, data.Length);
        }

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(MaterialIds);
            MaterialIds = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }

        ~GeometryComponent()
        {
            Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class GameEntityDescriptor : IDisposable
    {
        public TransformComponent Transform = new();
        public ScriptComponent Script = new();
        public GeometryComponent Geometry = new();

        public void Dispose()
        {
            Geometry.Dispose();
            GC.SuppressFinalize(this);
        }

        ~GameEntityDescriptor()
        {
            Dispose();
        }
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
    static partial class EngineAPI
    {
        private const string _engineDll = "EngineDll.dll";

        [LibraryImport(_engineDll)]
        public static partial EngineInitError InitializeEngine();
        [LibraryImport(_engineDll)]
        public static partial void ShutdownEngine();

        [LibraryImport(_engineDll, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        public static partial int LoadGameCodeDll(string dllPath);

        [LibraryImport(_engineDll)]
        public static partial int UnloadGameCodeDll();

        [LibraryImport(_engineDll, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        public static partial IntPtr GetScriptCreator(string name);

        [DllImport(_engineDll)]
        [return: MarshalAs(UnmanagedType.SafeArray)]
        public static extern string[] GetScriptNames();

        [LibraryImport(_engineDll)]
        public static partial int CreateRenderSurface(IntPtr host, int width, int height);

        [LibraryImport(_engineDll)]
        public static partial void RemoveRenderSurface(int surfaceId);

        [LibraryImport(_engineDll)]
        public static partial void ResizeRenderSurface(int surfaceId);

        [LibraryImport(_engineDll)]
        public static partial IntPtr GetWindowHandle(int surfaceId);

        [LibraryImport(_engineDll)]
        public static partial IdType CreateResource([In] byte[] data, int type);

        [LibraryImport(_engineDll)]
        public static partial void DestroyResource(IdType id, int type);

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

        [LibraryImport(_engineDll)]
        public static partial void RemoveShaderGroup(IdType id);

        [DllImport(_engineDll)]
        private static extern int CompileShader([In, Out] ShaderData data);

        public static void CompileShader(ShaderGroup shaderGroup)
        {
            Debug.Assert(!string.IsNullOrEmpty(shaderGroup?.Code));
            Debug.Assert(!string.IsNullOrEmpty(shaderGroup.FunctionName));
            Debug.Assert(shaderGroup.ExtraArgs?.Count > 0);
            Debug.Assert(shaderGroup.ByteCode.Count == 0);
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
                    data.ExtraArgs = args.Count > 0 ? string.Join(";", args) : string.Empty;
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

        [LibraryImport(_engineDll)]
        public static partial void SetGeometryIds(int surfaceId, [In] IdType[] geometryComponentIds, int count);

        [LibraryImport(_engineDll)]
        public static partial void RenderFrame(int surfaceId, IdType cameraId, ulong lightSet);

        [LibraryImport(_engineDll)]
        private static partial void UpdateEditorCamera(int surfaceId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ);

        public static void UpdateEditorCamera(int surfaceId, Vector3 position, Vector3 rotation)
        {
            UpdateEditorCamera(surfaceId, position.X, position.Y, position.Z, rotation.X, rotation.Y, rotation.Z);
        }

        [LibraryImport(_engineDll)]
        public static partial void SetCameraRange(int surfaceId, float nearZ, float farZ);

        [LibraryImport(_engineDll)]
        public static partial void SetCameraFoV(int surfaceId, float fov);

        [LibraryImport(_engineDll, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        public static partial ulong CreateLightSet(string lightSetKey);

        [LibraryImport(_engineDll, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
        public static partial void RemoveLightSet(ulong lightSetKey);

        // data = {
        //  u32         type;
        //  f32         intensity;
        //  math::v3    color;
        //  u32         is_enabled;
        //  math::u32v3 diffuse, specular, brdf_lut; (texture ids for ambient lights)
        //  f32         range; (for point and spot lights)
        //  math::v3    attenuation; (for point and spot lights)
        //  f32         umbra; (for spot lights)
        //  f32         penumbra; (for spot lights)
        // }
        //
        [LibraryImport(_engineDll)]
        private static partial IdType CreateLight(IdType entityId, ulong lightSetKey, [In] byte[] data, int dataSize);

        public static IdType CreateLight(Light light)
        {
            Debug.Assert(ID.IsValid(light?.EntityId ?? ID.INVALID_ID));
            Debug.Assert(!string.IsNullOrWhiteSpace(light.LightSetKey));
            using var writer = new BinaryWriter(new MemoryStream());

            writer.Write((int)light.Type);
            writer.Write(light.Intensity);
            writer.Write(light.Color.ScR);
            writer.Write(light.Color.ScG);
            writer.Write(light.Color.ScB);
            writer.Write(light.IsEnabled ? 1 : 0);

            if (light is AmbientLight ambientLight)
            {
                writer.Write(ambientLight.DiffuseContentId);
                writer.Write(ambientLight.SpecularContentId);
                writer.Write(ambientLight.BrdfLutContentId);
            }
            else if (light.Type != LightType.Directional)
            {
                var pointLight = light as PointLight;
                writer.Write(pointLight.Range);
                writer.Write(pointLight.Attenuation.X);
                writer.Write(pointLight.Attenuation.Y);
                writer.Write(pointLight.Attenuation.Z);

                if (light is Spotlight spotlight)
                {
                    writer.Write(spotlight.Umbra);
                    writer.Write(spotlight.Penumbra);
                }
            }

            writer.Flush();
            var data = (writer.BaseStream as MemoryStream).ToArray();

            var lightSetKey = LightSet.GetKey(light.LightSetKey);
            if (lightSetKey == LightSet.InvalidKey)
            {
                lightSetKey = LightSet.AddLightSet(light.LightSetKey, true);
            }
            Debug.Assert(lightSetKey != LightSet.InvalidKey);

            return CreateLight(light.EntityId, lightSetKey, data, data.Length);
        }

        [LibraryImport(_engineDll)]
        public static partial void RemoveLight(IdType lightId, ulong lightSetKey);

        [LibraryImport(_engineDll)]
        public static partial void GetLightIsEnabled([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] int[] isEnabled, int count);

        [LibraryImport(_engineDll)]
        public static partial void GetLightIntensity([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] float[] intensity, int count);

        [LibraryImport(_engineDll)]
        public static partial void GetLightRange([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] float[] range, int count);

        [LibraryImport(_engineDll)]
        public static partial void GetLightConeAngles([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] float[] umbra, [Out] float[] penumbra, int count);

        [LibraryImport(_engineDll)]
        public static partial void GetLightColor([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] float[] r, [Out] float[] g, [Out] float[] b, int count);

        [LibraryImport(_engineDll)]
        public static partial void GetLightAttenuation([In] IdType[] ids, [In] ulong[] lightSetKeys, [Out] float[] a, [Out] float[] b, [Out] float[] c, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightIsEnabled([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] int[] isEnabled, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightIntensity([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] float[] intensity, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightRange([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] float[] range, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightConeAngles([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] float[] umbra, [In] float[] penumbra, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightColor([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] float[] r, [In] float[] g, [In] float[] b, int count);

        [LibraryImport(_engineDll)]
        public static partial void SetLightAttenuation([In] IdType[] ids, [In] ulong[] lightSetKeys, [In] float[] a, [In] float[] b, [In] float[] c, int count);

        internal static partial class EntityAPI
        {
            [DllImport(_engineDll)]
            private static extern IdType CreateGameEntity(GameEntityDescriptor desc);
            public static IdType CreateGameEntity(GameEntity entity)
            {
                using GameEntityDescriptor desc = new();

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
                    //       has been loaded or not. This way, entities with a script component will be recreated after
                    //       the DLL has been loaded.
                    if (entity.GetComponent<Script>() is Script c && Project.Current != null)
                    {
                        if (Project.Current.AvailableScripts.Contains(c.Name))
                        {
                            desc.Script.ScriptCreator = GetScriptCreator(c.Name);
                        }
                        else
                        {
                            Logger.Log(MessageType.Error, $"Unable to find script with name {c.Name}. Game entity will be created without script component!");
                        }
                    }
                }
                // geometry component
                {
                    if (entity.GetComponent<Components.Geometry>() is Components.Geometry c)
                    {
                        Debug.Assert(c.MaterialsList.Count > 0 && ID.IsValid(c.ContentId));
                        desc.Geometry = new(c);
                    }
                }

                return CreateGameEntity(desc);
            }

            [LibraryImport(_engineDll)]
            public static partial void RemoveGameEntity(IdType id);

            [DllImport(_engineDll)]
            private static extern int UpdateComponent(IdType entityId, GameEntityDescriptor desc, ComponentType type);

            public static bool UpdateComponent(GameEntity entity, ComponentType type)
            {
                Debug.Assert(ID.IsValid(entity?.EntityId ?? ID.INVALID_ID));
                Debug.Assert(type != ComponentType.Transform);

                using GameEntityDescriptor desc = new();

                switch (type)
                {
                    case ComponentType.Transform: return false;
                    case ComponentType.Script:
                        {
                            if (entity.GetComponent<Script>() is Script c)
                            {
                                Debug.Assert(Project.Current != null);
                                if (Project.Current.AvailableScripts.Contains(c.Name))
                                {
                                    desc.Script.ScriptCreator = GetScriptCreator(c.Name);
                                }
                                else
                                {
                                    Logger.Log(MessageType.Error, $"Unable to find script with name {c.Name}. Game entity will be created without script component!");
                                }
                            }
                        }
                        break;
                    case ComponentType.Geometry:
                        {
                            if (entity.GetComponent<Components.Geometry>() is Components.Geometry c)
                            {
                                Debug.Assert(c.MaterialsList.Count > 0 && ID.IsValid(c.ContentId));
                                desc.Geometry = new(c);
                            }
                        }
                        break;
                    default:
                        break;
                }

                return UpdateComponent(entity.EntityId, desc, type) != 0;
            }

            [LibraryImport(_engineDll)]
            public static partial IdType GetComponentId(IdType entityId, ComponentType type);

            private delegate void GetTransformAPI(IdType[] ids, float[] x, float[] y, float[] z, int count);

            private static Vector3[] GetTransform(IdType[] ids, GetTransformAPI action, bool wrapAngles = false)
            {
                var count = ids.Length;
                var x = new float[count]; var y = new float[count]; var z = new float[count];
                action(ids, x, y, z, count);

                if (wrapAngles)
                {
                    for (int i = 0; i < count; ++i)
                    {
                        x[i] = MathUtil.WrapAngle(x[i]);
                        y[i] = MathUtil.WrapAngle(y[i]);
                        z[i] = MathUtil.WrapAngle(z[i]);
                    }
                }

                var result = new Vector3[count];
                for (int i = 0; i < count; ++i)
                {
                    result[i] = new(x[i], y[i], z[i]);
                }

                return result;
            }

            [LibraryImport(_engineDll)]
            private static partial void GetPosition([In] IdType[] ids, [Out] float[] x, [Out] float[] y, [Out] float[] z, int count);

            public static Vector3[] GetPosition(IdType[] ids) => GetTransform(ids, GetPosition);

            [LibraryImport(_engineDll)]
            private static partial void GetRotation([In] IdType[] ids, [Out] float[] x, [Out] float[] y, [Out] float[] z, int count);

            public static Vector3[] GetRotation(IdType[] ids) => GetTransform(ids, GetRotation, true);

            [LibraryImport(_engineDll)]
            private static partial void GetScale([In] IdType[] ids, [Out] float[] x, [Out] float[] y, [Out] float[] z, int count);

            public static Vector3[] GetScale(IdType[] ids) => GetTransform(ids, GetScale);

            [LibraryImport(_engineDll)]
            public static partial void SetPosition([In] IdType[] ids, [In] float[] x, [In] float[] y, [In] float[] z, int count, int frame);

            [LibraryImport(_engineDll)]
            public static partial void SetRotation([In] IdType[] ids, [In] float[] x, [In] float[] y, [In] float[] z, int count, int frame);

            [LibraryImport(_engineDll)]
            public static partial void SetScale([In] IdType[] ids, [In] float[] x, [In] float[] y, [In] float[] z, int count, int _);
        }
    }
}