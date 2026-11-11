// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Content;

class Skeleton : Asset
{
    private byte[] _rawData = [];
    private readonly List<string> _jointNames = [];
    private readonly List<int> _jointHierarchy = [];
    private readonly List<Matrix4x4> _inverseBindPoses = [];

    public IReadOnlyList<string> JointNames => _jointNames.AsReadOnly();
    public IReadOnlyList<int> JointHierarchy => _jointHierarchy.AsReadOnly();
    public IReadOnlyList<Matrix4x4> InverseBindPoses => _inverseBindPoses.AsReadOnly();

    public override AssetMetadata GetMetadata()
    {
        return null;
    }

    public override bool Import(string file)
    {
        throw new NotImplementedException();
    }

    public override bool Load(string file)
    {
        Debug.Assert(File.Exists(file));
        Debug.Assert(Path.GetExtension(file).Equals(AssetFileExtension, StringComparison.OrdinalIgnoreCase));
        using var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read));

        ReadAssetFileHeader(reader);
        var dataLength = reader.ReadInt32();
        if (dataLength > 0 && reader.ReadBytes(dataLength) is { } rawData)
        {
            UnpackJointData(rawData);
            return true;
        }

        throw new InvalidDataException($"Error while reading joint data from {file}");
    }

    public override byte[] PackForEngine()
    {
        throw new NotImplementedException();
    }

    public override IEnumerable<string> Save(string file)
    {
        Debug.Assert(_rawData.Length > 0);
        var savedFiles = new List<string>();
        if (_rawData.Length == 0) return savedFiles;
        var path = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar;
        var fileName = Path.GetFileNameWithoutExtension(file);

        try
        {
            var skeletonFileName = ContentHelper.SanitizeFileName($"{path}{fileName}_joints") + AssetFileExtension;
            Guid = TryGetAssetInfo(skeletonFileName) is { } info && info.Type == Type ? info.Guid : Guid.NewGuid();
            Hash = ContentHelper.ComputeHash(_rawData);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Resources/Content/Skeleton.png");
            bmp.DecodePixelWidth = ContentInfo.IconWidth;
            bmp.EndInit();
            Icon = BitmapHelper.CreateThumbnail(bmp, ContentInfo.IconWidth, ContentInfo.IconWidth);

            using (var writer = new BinaryWriter(File.Open(skeletonFileName, FileMode.Create, FileAccess.Write)))
            {
                WriteAssetFileHeader(writer);
                writer.Write(_rawData.Length);
                writer.Write(_rawData);
            }

            Logger.Log(MessageType.Info, $"Saved skeleton to {skeletonFileName}");
            savedFiles.Add(skeletonFileName);
            FullPath = skeletonFileName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to save skeleton to {file}\n{ex.Message}");
        }

        return savedFiles;
    }

    // Expects data to contain:
    // u32 num_joints
    // struct{
    //   u32    name_length
    //   u8[]   name_string
    // } names[num_joints]
    // u16[num_joints] joint_hierarchy
    // mat::m4x4[num_joints] inverse_bind_poses
    //
    private void UnpackJointData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new InvalidDataException("Missing joint data");

        _rawData = new byte[data.Length];
        Array.Copy(data, _rawData, data.Length);
        using var reader = new BinaryReader(new MemoryStream(data));
        var numJoints = reader.ReadInt32();
        if (numJoints <= 0) throw new InvalidDataException("Invalid number of joints");

        for (int i = 0; i < numJoints; ++i)
        {
            var nameLength = reader.ReadInt32();
            if (nameLength > 0)
            {
                var nameBytes = reader.ReadBytes(nameLength);
                var jointName = Encoding.UTF8.GetString(nameBytes);
                _jointNames.Add(!string.IsNullOrWhiteSpace(jointName) ? jointName : $"joint{i:D3}");
            }
        }

        for (int i = 0; i < numJoints; ++i)
        {
            var index = reader.ReadUInt16();
            _jointHierarchy.Add(index);
        }

        for (int i = 0; i < numJoints; ++i)
        {
            var matrix = new Matrix4x4();
            for (int row = 0; row < 4; ++row)
            {
                for (int col = 0; col < 4; ++col)
                {
                    matrix[row, col] = reader.ReadSingle();
                }
            }

            _inverseBindPoses.Add(matrix);
        }
    }

    public Skeleton() : base(AssetType.Skeleton) { }
    public Skeleton(AssetInfo assetInfo) : this()
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        Debug.Assert(File.Exists(assetInfo.FullPath) && assetInfo.Type == Type);
        Load(assetInfo.FullPath);
    }

    public Skeleton(byte[] data) : this()
    {
        Debug.Assert(data?.Length > 0);
        UnpackJointData(data);
    }

}
