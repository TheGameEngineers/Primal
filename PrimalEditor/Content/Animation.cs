using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Content;

class Animation : Asset
{
    private static int _nameCounter = 0;
    private byte[] _rawData = [];
    private Guid _skeletonGuid;

    public string ClipName
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ClipName));
            }
        }
    } = $"AnimationClip{++_nameCounter:D3}";

    public float FramesPerSecond
    {
        get;
        private set
        {
            value = float.Max(value, 0);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(FramesPerSecond));
            }
        }
    } = 0f;

    public int FrameCount
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(FrameCount));
            }
        }
    } = 0;

    public Vector3[][] TranslationsPerJoint { get; private set; } = [];
    public Vector4[][] RotationsPerJoint { get; private set; } = [];
    public Vector3[][] ScalesPerJoint { get; private set; } = [];

    public override AssetMetadata GetMetadata()
    {
        throw new NotImplementedException();
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
        _skeletonGuid = GuidExtensions.ReadFromBinary(reader);
        var dataLength = reader.ReadInt32();
        if (dataLength > 0 && reader.ReadBytes(dataLength) is { } rawData)
        {
            UnpackAnimationData(rawData);
            return true;
        }

        throw new InvalidDataException($"Error while reading animation data from {file}");
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
            var animationFileName = ContentHelper.SanitizeFileName($"{path}{fileName}_{ClipName.Replace(' ', '_')}") + AssetFileExtension;
            Guid = TryGetAssetInfo(animationFileName) is { } info && info.Type == Type ? info.Guid : Guid.NewGuid();
            Hash = ContentHelper.ComputeHash(_rawData);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Resources/Content/Animation.png");
            bmp.DecodePixelWidth = ContentInfo.IconWidth;
            bmp.EndInit();
            Icon = BitmapHelper.CreateThumbnail(bmp, ContentInfo.IconWidth, ContentInfo.IconWidth);

            using (var writer = new BinaryWriter(File.Open(animationFileName, FileMode.Create, FileAccess.Write)))
            {
                WriteAssetFileHeader(writer);
                _skeletonGuid.WriteToBinary(writer);
                writer.Write(_rawData.Length);
                writer.Write(_rawData);
            }

            Logger.Log(MessageType.Info, $"Saved animation to {animationFileName}");
            savedFiles.Add(animationFileName);
            FullPath = animationFileName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to save skeleton to {file}\n{ex.Message}");
        }

        return savedFiles;
    }

    // Expects data to contain:
    // u32  num_joints
    // u32  clip_name_length
    // u8*  clip_name_string
    // f32  frames_per_second
    // u32  frame_count
    // math::v3[joint_count * frame_count] translations
    // math::v4[joint_count * frame_count] rotation_quaternions
    // math::v3[joint_count * frame_count] scales
    //
    private void UnpackAnimationData(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new InvalidDataException("Missing joint data");
        _rawData = data;
        using var reader = new BinaryReader(new MemoryStream(data));
        var numJoints = reader.ReadInt32();
        if (numJoints <= 0) throw new InvalidDataException("Invalid number of joints");

        var nameLength = reader.ReadInt32();
        var clipName = "AnimationClip";
        if (nameLength > 0)
        {
            var nameBytes = reader.ReadBytes(nameLength);
            clipName = Encoding.UTF8.GetString(nameBytes);
        }

        ClipName = clipName;
        FramesPerSecond = reader.ReadSingle();
        if (FramesPerSecond == 0) throw new InvalidDataException("Invalid frame rate for animation clip.");
        FrameCount = reader.ReadInt32();
        if (FrameCount == 0) throw new InvalidDataException("Invalid frame count for animation clip.");

        TranslationsPerJoint = new Vector3[numJoints][];
        for (int i = 0; i < numJoints; ++i)
        {
            var translations = new Vector3[FrameCount];
            for (int j = 0; j < FrameCount; ++j)
            {
                Vector3 translation;
                translation.X = reader.ReadSingle();
                translation.Y = reader.ReadSingle();
                translation.Z = reader.ReadSingle();
                translations[j] = translation;
            }
            TranslationsPerJoint[i] = translations;
        }

        RotationsPerJoint = new Vector4[numJoints][];
        for (int i = 0; i < numJoints; ++i)
        {
            var rotations = new Vector4[FrameCount];
            for (int j = 0; j < FrameCount; ++j)
            {
                Vector4 rotation;
                rotation.X = reader.ReadSingle();
                rotation.Y = reader.ReadSingle();
                rotation.Z = reader.ReadSingle();
                rotation.W = reader.ReadSingle();
                rotations[j] = rotation;
            }
            RotationsPerJoint[i] = rotations;
        }

        ScalesPerJoint = new Vector3[numJoints][];
        for (int i = 0; i < numJoints; ++i)
        {
            var scales = new Vector3[FrameCount];
            for (int j = 0; j < FrameCount; ++j)
            {
                Vector3 scale;
                scale.X = reader.ReadSingle();
                scale.Y = reader.ReadSingle();
                scale.Z = reader.ReadSingle();
                scales[j] = scale;
            }
            ScalesPerJoint[i] = scales;
        }
    }

    public Animation() : base(AssetType.Animation) { }

    public Animation(AssetInfo assetInfo) : this()
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        Debug.Assert(File.Exists(assetInfo.FullPath) && assetInfo.Type == Type);
        Load(assetInfo.FullPath);
    }

    public Animation(byte[] data, Guid skeletonGuid) : this()
    {
        if (skeletonGuid == Guid.Empty) throw new InvalidDataException("Invalid skeleton GUID");
        Debug.Assert(data?.Length > 0);
        _skeletonGuid = skeletonGuid;
        UnpackAnimationData(data);
    }
}
