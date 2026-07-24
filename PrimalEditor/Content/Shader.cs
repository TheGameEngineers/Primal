// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PrimalEditor.Content;

[Flags]
enum ShaderFlags : int
{
    None = 0x0,
    Vertex = 0x01,
    Hull = 0x02,
    Domain = 0x04,
    Geometry = 0x08,
    Pixel = 0x10,
    Compute = 0x20,
    Amplification = 0x40,
    Mesh = 0x80,
}

enum ShaderType : int
{
    Vertex = 0,
    Hull,
    Domain,
    Geometry,
    Pixel,
    Compute,
    Amplification,
    Mesh,
}

class ShaderGroup
{
    private class UploadedShaderGroup
    {
        public IdType ContentId { get; init; } = ID.INVALID_ID;
        public byte[] CombinedHashes { get; init; }
        public int ReferenceCount { get; private set; }

        private static readonly Lock _lock = new();
        private static readonly Dictionary<string, UploadedShaderGroup> _uploadedShaders = [];
        private static readonly Dictionary<IdType, UploadedShaderGroup> _uploadedShaderIds = [];

        public static UploadedShaderGroup UploadToEngine(ShaderGroup shaderGroup)
        {
            if (shaderGroup.Count == 0 || shaderGroup.ByteCode.Any(x => x.Length == 0) ||
                shaderGroup.Hash.Any(x => x.Length == 0))
            {
                return null;
            }

            lock (_lock)
            {
                var combinedHashes = shaderGroup.Hash.SelectMany(x => x).ToArray();

                if (ID.IsValid(shaderGroup.ContentId) && _uploadedShaderIds.TryGetValue(shaderGroup.ContentId, out var uploadedShader))
                {

                    if (uploadedShader.CombinedHashes.SequenceEqual(combinedHashes))
                    {
                        ++uploadedShader.ReferenceCount;
                        return uploadedShader;
                    }
                    else
                    {
                        UnloadFromEngine(uploadedShader.ContentId);
                    }
                }
                else
                {
                    Debug.Assert(!ID.IsValid(shaderGroup.ContentId));
                }

                var hashString = Convert.ToBase64String(combinedHashes);

                if (_uploadedShaders.TryGetValue(hashString, out var identicalShader))
                {
                    ++identicalShader.ReferenceCount;
                    return identicalShader;
                }

                var newUploadedShader = new UploadedShaderGroup()
                {
                    ContentId = EngineAPI.AddShaderGroup(shaderGroup),
                    CombinedHashes = combinedHashes,
                    ReferenceCount = 1
                };

                Debug.Assert(ID.IsValid(newUploadedShader.ContentId));

                _uploadedShaders.Add(hashString, newUploadedShader);
                _uploadedShaderIds.Add(newUploadedShader.ContentId, newUploadedShader);

                return newUploadedShader;
            }
        }

        public static void UnloadFromEngine(IdType id)
        {
            lock (_lock)
            {
                Debug.Assert(ID.IsValid(id) && _uploadedShaderIds.ContainsKey(id));

                if (ID.IsValid(id) && _uploadedShaderIds.TryGetValue(id, out var uploadedShader))
                {
                    Debug.Assert(uploadedShader.ReferenceCount > 0);
                    --uploadedShader.ReferenceCount;

                    if (uploadedShader.ReferenceCount == 0)
                    {
                        EngineAPI.RemoveShaderGroup(uploadedShader.ContentId);
                        var hashString = Convert.ToBase64String(uploadedShader.CombinedHashes);
                        Debug.Assert(_uploadedShaders.ContainsKey(hashString));
                        _uploadedShaders.Remove(hashString);
                        _uploadedShaderIds.Remove(uploadedShader.ContentId);
                    }
                }
            }
        }

        private UploadedShaderGroup() { }
    }

    public static readonly int HashSize = 16;

    public ShaderType Type { get; set; }
    public string Code { get; set; }
    public string FunctionName { get; set; }
    public List<List<string>> ExtraArgs { get; set; } = [];
    public List<uint> Keys { get; set; } = [];
    public List<byte[]> ByteCode { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Assembly { get; set; } = [];
    public List<byte[]> Hash { get; set; } = [];

    public IdType ContentId { get; private set; } = ID.INVALID_ID;

    private UploadedShaderGroup _uploadedShader;

    public int Count
    {
        get
        {
            Debug.Assert(new int[] { ExtraArgs.Count, Keys.Count, ByteCode.Count, Errors.Count, Assembly.Count, Hash.Count }.Distinct().Count() == 1);
            return Keys.Count;
        }
    }

    public void ToBinary(BinaryWriter writer)
    {
        writer.Write((int)Type);
        writer.Write(Code);
        writer.Write(FunctionName);
        writer.Write(Count);

        ExtraArgs.ForEach(args => writer.Write(string.Join(";", args)));
        PackForEngine(writer);
        Errors.ForEach(writer.Write);
        Assembly.ForEach(writer.Write);
    }

    public void FromBinary(BinaryReader reader)
    {
        ExtraArgs.Clear();
        Keys.Clear();
        ByteCode.Clear();
        Errors.Clear();
        Assembly.Clear();
        Hash.Clear();

        Type = (ShaderType)reader.ReadInt32();
        Code = reader.ReadString();
        FunctionName = reader.ReadString();
        var count = reader.ReadInt32();

        ExtraArgs.AddRange(Enumerable.Range(0, count).Select(_ => reader.ReadString().Split(";").ToList()));
        Keys.AddRange(Enumerable.Range(0, count).Select(_ => reader.ReadUInt32()));

        for (int i = 0; i < count; i++)
        {
            // NOTE: byteCodeLength is a 64-bit value!
            var byteCodeLength = reader.ReadInt64();
            if (byteCodeLength > 0)
            {
                Hash.Add(reader.ReadBytes(HashSize));
                ByteCode.Add(reader.ReadBytes((int)byteCodeLength));
            }
        }

        Errors.AddRange(Enumerable.Range(0, count).Select(_ => reader.ReadString()));
        Assembly.AddRange(Enumerable.Range(0, count).Select(_ => reader.ReadString()));
    }

    private void PackForEngine(BinaryWriter writer)
    {
        Keys.ForEach(key => writer.Write(key));

        for (int i = 0; i < Count; i++)
        {
            // NOTE: byteCodeLength is a 64-bit value!
            var byteCodeLength = ByteCode[i].LongLength;
            writer.Write(byteCodeLength);
            if (byteCodeLength > 0)
            {
                writer.Write(Hash[i]);
                writer.Write(ByteCode[i]);
            }
        }
    }

    public byte[] PackForEngine()
    {
        if (Count == 0 || ByteCode.Any(x => x.Length == 0) || Hash.Any(x => x.Length == 0))
        {
            return null;
        }

        using var writer = new BinaryWriter(new MemoryStream());
        PackForEngine(writer);
        writer.Flush();

        return (writer.BaseStream as MemoryStream).ToArray();
    }

    public IdType UploadToEngine()
    {
        var uploadedShader = UploadedShaderGroup.UploadToEngine(this);
        Debug.Assert(uploadedShader != null && ID.IsValid(uploadedShader.ContentId));

        if (uploadedShader == null || !ID.IsValid(uploadedShader.ContentId))
        {
            return ID.INVALID_ID;
        }

        _uploadedShader = uploadedShader;
        ContentId = uploadedShader.ContentId;

        return ContentId;
    }

    public void UnloadFromEngine()
    {
        Debug.Assert(ID.IsValid(ContentId) && _uploadedShader != null);

        UploadedShaderGroup.UnloadFromEngine(ContentId);
        if (_uploadedShader.ReferenceCount == 0)
        {
            ContentId = ID.INVALID_ID;
            _uploadedShader = null;
        }
    }
}