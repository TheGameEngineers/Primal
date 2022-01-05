// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrimalEditor.Content
{
    enum AssetType
    {
        Unknown,
        Animation,
        Audio,
        Material,
        Mesh,
        Skeleton,
        Texture,
    }

    sealed class AssetInfo
    {
        public AssetType Type { get; set; }
        public byte[] Icon { get; set; }
        public string FullPath { get; set; }
        public string FileName => Path.GetFileNameWithoutExtension(FullPath);
        public string SourcePath { get; set; }
        public DateTime RegisterTime { get; set; }
        public DateTime ImportDate { get; set; }
        public Guid Guid { get; set; }
        public byte[] Hash { get; set; }
    }

    abstract class Asset : ViewModelBase
    {
        public static string AssetFileExtension => ".asset";
        public AssetType Type { get; private set; }
        public byte[] Icon { get; protected set; }
        public string SourcePath { get; protected set; }

        private string _fullPath;
        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath != value)
                {
                    _fullPath = value;
                    OnPropertyChanged(nameof(FullPath));
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        public string FileName => Path.GetFileNameWithoutExtension(FullPath);
        public Guid Guid { get; protected set; } = Guid.NewGuid();
        public DateTime ImportDate { get; protected set; }
        public byte[] Hash { get; protected set; }

        public abstract IEnumerable<string> Save(string file);

        private static AssetInfo GetAssetInfo(BinaryReader reader)
        {
            reader.BaseStream.Position = 0;
            var info = new AssetInfo();

            info.Type = (AssetType)reader.ReadInt32();
            var idSize = reader.ReadInt32();
            info.Guid = new Guid(reader.ReadBytes(idSize));
            info.ImportDate = DateTime.FromBinary(reader.ReadInt64());
            var hashSize = reader.ReadInt32();
            if(hashSize>0)
            {
                info.Hash = reader.ReadBytes(hashSize);
            }
            info.SourcePath = reader.ReadString();
            var iconSize = reader.ReadInt32();
            info.Icon = reader.ReadBytes(iconSize);

            return info;
        }

        public static AssetInfo GetAssetInfo(string file)
        {
            Debug.Assert(File.Exists(file) && Path.GetExtension(file) == AssetFileExtension);
            try
            {
                using var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read));
                var info = GetAssetInfo(reader);
                info.FullPath = file;
                return info;
            }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }

            return null;
        }

        protected void WriteAssetFileHeader(BinaryWriter writer)
        {
            var id = Guid.ToByteArray();
            var importDate = DateTime.Now.ToBinary();

            writer.BaseStream.Position = 0;

            writer.Write((int)Type);
            writer.Write(id.Length);
            writer.Write(id);
            writer.Write(importDate);
            // asset hash is optional
            if (Hash?.Length > 0)
            {
                writer.Write(Hash.Length);
                writer.Write(Hash);
            }
            else
            {
                writer.Write(0);
            }

            writer.Write(SourcePath ?? "");
            writer.Write(Icon.Length);
            writer.Write(Icon);
        }

        protected void ReadAssetFileHeader(BinaryReader reader)
        {
            var info = GetAssetInfo(reader);

            Debug.Assert(Type == info.Type);
            Guid = info.Guid;
            ImportDate = info.ImportDate;
            Hash = info.Hash;
            SourcePath = info.SourcePath;
            Icon = info.Icon;
        }

        public Asset(AssetType type)
        {
            Debug.Assert(type != AssetType.Unknown);
            Type = type;
        }
    }
}
