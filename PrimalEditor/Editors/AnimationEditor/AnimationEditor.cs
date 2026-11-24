// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PrimalEditor.Editors;

[Flags]
enum AnimationChannels
{
    PositionX = 0x0001,
    PositionY = 0x0002,
    PositionZ = 0x0004,
    RotationX = 0x0008,
    RotationY = 0x0010,
    RotationZ = 0x0020,
    ScaleX = 0x0040,
    ScaleY = 0x0080,
    ScaleZ = 0x0100,

    PositionMask = PositionX | PositionY | PositionZ,
    RotationMask = RotationX | RotationY | RotationZ,
    ScaleMask = ScaleX | ScaleY | ScaleZ,
}

class AnimationEditor : ViewModelBase, IAssetEditor
{
    private static readonly Color _editorRed = (Color)Application.Current.Resources["Editor.RedColor"];
    private static readonly Color _editorGreen = (Color)Application.Current.Resources["Editor.GreenColor"];
    private static readonly Color _editorBlue = (Color)Application.Current.Resources["Editor.BlueColor"];

    private Guid _assetGuid = Guid.Empty;

    public AssetEditorState State
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public Asset Asset => Animation;

    public Animation Animation
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged();
            }
        }
    }

    public SkeletonEditor SkeletonEditor { get; } = new();

    public Skeleton Skeleton => SkeletonEditor?.Skeleton;

    public readonly ObservableCollection<ChartSeries> _series = [];

    public ReadOnlyObservableCollection<ChartSeries> Series { get; }

    public void ShowChannelsForJoint(int jointIndex, AnimationChannels channels)
    {
        Debug.Assert(jointIndex >= 0 &&
            jointIndex < Animation.TranslationsPerJoint.Length &&
            jointIndex < Animation.RotationsPerJoint.Length &&
            jointIndex < Animation.ScalesPerJoint.Length &&
            jointIndex < Skeleton.JointHierarchy.Count);
        if (jointIndex < 0 ||
            jointIndex >= Animation.TranslationsPerJoint.Length ||
            jointIndex >= Animation.RotationsPerJoint.Length ||
            jointIndex >= Animation.ScalesPerJoint.Length ||
            jointIndex >= Skeleton.JointHierarchy.Count) return;

        _series.Clear();

        MakeSeries(Animation.TranslationsPerJoint[jointIndex], channels, AnimationChannels.PositionMask);
        MakeSeries(EulerFromQuaternion(Animation.RotationsPerJoint[jointIndex]), channels, AnimationChannels.RotationMask);
        MakeSeries(Animation.ScalesPerJoint[jointIndex], channels, AnimationChannels.ScaleMask);
    }

    private void MakeSeries(Vector3[] values, AnimationChannels channels, AnimationChannels groupMask)
    {
        var (groupName, xChannel, yChannel, zChannel) = groupMask switch
        {
            AnimationChannels.PositionMask =>
                ("Position", AnimationChannels.PositionX, AnimationChannels.PositionY, AnimationChannels.PositionZ),
            AnimationChannels.RotationMask =>
                ("Rotation", AnimationChannels.RotationX, AnimationChannels.RotationY, AnimationChannels.RotationZ),
            _ => ("Scale", AnimationChannels.ScaleX, AnimationChannels.ScaleY, AnimationChannels.ScaleZ)
        };

        void AddAxisSeries(string name, Color color, AnimationChannels channel, Func<Vector3, float> component)
        {
            if ((channels & channel) == 0) return;

            var chartSeries = new ChartSeries
            {
                Name = name,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 1.0
            };

            for (int frame = 0; frame < values.Length; ++frame)
            {
                chartSeries.Points.Add(new(frame, component(values[frame])));
            }

            _series.Add(chartSeries);
        }

        AddAxisSeries($"{groupName} X", _editorRed, xChannel, static value => value.X);
        AddAxisSeries($"{groupName} Y", _editorGreen, yChannel, static value => value.Y);
        AddAxisSeries($"{groupName} Z", _editorBlue, zChannel, static value => value.Z);
    }

    private static Vector3[] EulerFromQuaternion(Vector4[] rotations)
    {
        var eulerAngles = new Vector3[rotations.Length];
        for (int i = 0; i < rotations.Length; ++i)
        {
            var rotation = rotations[i];
            var x = rotation.X;
            var y = rotation.Y;
            var z = rotation.Z;
            var w = rotation.W;

            var roll = float.Atan2(2.0f * (w * x + y * z), 1.0f - 2.0f * (x * x + y * y));
            var sinPitch = float.Clamp(2.0f * (w * y - z * x), -1.0f, 1.0f);
            var pitch = float.Asin(sinPitch);
            var yaw = float.Atan2(2.0f * (w * z + x * y), 1.0f - 2.0f * (y * y + z * z));

            eulerAngles[i] = new(float.RadiansToDegrees(roll), float.RadiansToDegrees(pitch), float.RadiansToDegrees(yaw));
        }

        return eulerAngles;
    }

    public bool CheckAssetGuid(Guid guid) => _assetGuid == guid;

    public async Task SetAsset(AssetInfo info)
    {
        try
        {
            Debug.Assert(info != null && File.Exists(info.FullPath));
            _assetGuid = info.Guid;
            var animation = new Animation();
            State = AssetEditorState.Loading;

            await Task.Run(() => animation.Load(info.FullPath));
            Animation = animation;
            var skeletonInfo = AssetRegistry.GetAssetInfo(animation.SkeletonGuid);
            await SkeletonEditor.SetAsset(skeletonInfo);
            ShowChannelsForJoint(0, AnimationChannels.PositionX);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to set animation for use in animation editor. File: {info.FullPath}\n{ex.Message}");
        }
        finally { State = AssetEditorState.Done; }
    }

    public AnimationEditor()
    {
        Series = new(_series);
    }
}
