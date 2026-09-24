// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.Utilities;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;

namespace PrimalEditor.Components;

enum Space
{
    Absolute,
    Local,
    World
}

[DataContract]
class Transform(GameEntity owner) : Component(owner)
{
    private Vector3 _position;
    [DataMember]
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                OnPropertyChanged(nameof(Position));
            }
        }
    }

    private Vector3 _rotation;
    [DataMember]
    public Vector3 Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation != value)
            {
                _rotation = value;
                OnPropertyChanged(nameof(Rotation));
            }
        }
    }

    private Vector3 _scale = Vector3.One;
    [DataMember]
    public Vector3 Scale
    {
        get => _scale;
        set
        {
            if (_scale != value)
            {
                _scale = value;
                OnPropertyChanged(nameof(Scale));
            }
        }
    }

    public override IMSComponent GetMultiselectionComponent(MSEntity msEntity) => new MSTransform(msEntity);
}

sealed class MSTransform : MSComponent<Transform>
{
    public float? PosX
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(PosX));
    }
    public float? PosY
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(PosY));
    }
    public float? PosZ
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(PosZ));
    }
    public float? RotX
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(RotX));
    }
    public float? RotY
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(RotY));
    }
    public float? RotZ
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(RotZ));
    }
    public float? ScaleX
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(ScaleX));
    }
    public float? ScaleY
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(ScaleY));
    }
    public float? ScaleZ
    {
        get;
        set => SetPropertyValue(ref field, value, nameof(ScaleZ));
    }

    private static bool _isLocalRotation = true;
    public bool IsLocalRotation
    {
        get => _isLocalRotation;
        set
        {
            if (_isLocalRotation != value)
            {
                _isLocalRotation = value;
                OnPropertyChanged(nameof(IsLocalRotation));
            }
        }
    }

    private static bool _isUniformScale = true;
    public bool IsUniformScale
    {
        get => _isUniformScale;
        set
        {
            if (_isUniformScale != value)
            {
                _isUniformScale = value;
                OnPropertyChanged(nameof(IsUniformScale));
            }
        }
    }

    private Vector3 _previousLocalPos;
    private Vector3 _localPos;

    public float LocalPosX
    {
        get => _localPos.X;
        set
        {
            if (!_localPos.X.IsTheSameAs(value))
            {
                _localPos.X = value;
                OnPropertyChanged(nameof(LocalPosX));
            }
        }
    }

    public float LocalPosY
    {
        get => _localPos.Y;
        set
        {
            if (!_localPos.Y.IsTheSameAs(value))
            {
                _localPos.Y = value;
                OnPropertyChanged(nameof(LocalPosY));
            }
        }
    }

    public float LocalPosZ
    {
        get => _localPos.Z;
        set
        {
            if (!_localPos.Z.IsTheSameAs(value))
            {
                _localPos.Z = value;
                OnPropertyChanged(nameof(LocalPosZ));
            }
        }
    }

    private Vector3 _previousRotOffset;
    private Vector3 _rotOffset;

    public float RotOffsetX
    {
        get => _rotOffset.X;
        set
        {
            if (!_rotOffset.X.IsTheSameAs(value))
            {
                _rotOffset.X = value;
                OnPropertyChanged(nameof(RotOffsetX));
            }
        }
    }

    public float RotOffsetY
    {
        get => _rotOffset.Y;
        set
        {
            if (!_rotOffset.Y.IsTheSameAs(value))
            {
                _rotOffset.Y = value;
                OnPropertyChanged(nameof(RotOffsetY));
            }
        }
    }

    public float RotOffsetZ
    {
        get => _rotOffset.Z;
        set
        {
            if (!_rotOffset.Z.IsTheSameAs(value))
            {
                _rotOffset.Z = value;
                OnPropertyChanged(nameof(RotOffsetZ));
            }
        }
    }

    private void SetPropertyValue(ref float? field, float? value, string propertyName)
    {
        if (value.HasValue)
        {
            value = float.Round(value.Value, 3);
        }

        if (!field.IsTheSameAs(value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    private void ResetLocalFrame()
    {
        _previousLocalPos = Vector3.Zero;
        LocalPosX = LocalPosY = LocalPosZ = 0;

        _previousRotOffset = Vector3.Zero;
        RotOffsetX = RotOffsetY = RotOffsetZ = 0;
    }

    protected override bool UpdateComponents(string propertyName)
    {
        var count = SelectedComponents.Count;
        var componentIds = SelectedComponents.Select(c => c.Owner.EntityId).ToArray();
        Debug.Assert(count == componentIds.Length);
        float[] x = new float[count], y = new float[count], z = new float[count];
        var index = 0;

        switch (propertyName)
        {
            case nameof(PosX):
            case nameof(PosY):
            case nameof(PosZ):
                SelectedComponents.ForEach(c =>
                {
                    var pos = new Vector3(PosX ?? c.Position.X, PosY ?? c.Position.Y, PosZ ?? c.Position.Z);
                    x[index] = pos.X; y[index] = pos.Y; z[index] = pos.Z; ++index;
                    c.Position = pos;
                });

                EngineAPI.EntityAPI.SetPosition(componentIds, x, y, z, count, (int)Space.Absolute);
                return true;

            case nameof(RotX):
            case nameof(RotY):
            case nameof(RotZ):
                SelectedComponents.ForEach(c =>
                {
                    var rot = new Vector3(RotX ?? c.Rotation.X, RotY ?? c.Rotation.Y, RotZ ?? c.Rotation.Z);
                    x[index] = rot.X; y[index] = rot.Y; z[index] = rot.Z; ++index;
                    c.Rotation = rot;
                });

                EngineAPI.EntityAPI.SetRotation(componentIds, x, y, z, count, (int)Space.Absolute);
                return true;

            case nameof(ScaleX):
            case nameof(ScaleY):
            case nameof(ScaleZ):
                SelectedComponents.ForEach(c =>
                {
                    var scale = new Vector3(ScaleX ?? c.Scale.X, ScaleY ?? c.Scale.Y, ScaleZ ?? c.Scale.Z);
                    x[index] = scale.X; y[index] = scale.Y; z[index] = scale.Z; ++index;
                    c.Scale = scale;
                });

                EngineAPI.EntityAPI.SetScale(componentIds, x, y, z, count, (int)Space.Local);
                return true;

            case nameof(LocalPosX):
            case nameof(LocalPosY):
            case nameof(LocalPosZ):
                {
                    var dx = !_previousLocalPos.X.IsTheSameAs(_localPos.X) ? _localPos.X - _previousLocalPos.X : 0f;
                    var dy = !_previousLocalPos.Y.IsTheSameAs(_localPos.Y) ? _localPos.Y - _previousLocalPos.Y : 0f;
                    var dz = !_previousLocalPos.Z.IsTheSameAs(_localPos.Z) ? _localPos.Z - _previousLocalPos.Z : 0f;

                    _previousLocalPos = _localPos;
                    Array.Fill(x, dx); Array.Fill(y, dy); Array.Fill(z, dz);
                    EngineAPI.EntityAPI.SetPosition(componentIds, x, y, z, count, (int)Space.Local);
                }
                return true;

            case nameof(RotOffsetX):
            case nameof(RotOffsetY):
            case nameof(RotOffsetZ):
                {
                    var dx = !_previousRotOffset.X.IsTheSameAs(_rotOffset.X) ? _rotOffset.X - _previousRotOffset.X : 0f;
                    var dy = !_previousRotOffset.Y.IsTheSameAs(_rotOffset.Y) ? _rotOffset.Y - _previousRotOffset.Y : 0f;
                    var dz = !_previousRotOffset.Z.IsTheSameAs(_rotOffset.Z) ? _rotOffset.Z - _previousRotOffset.Z : 0f;

                    _previousRotOffset = _rotOffset;
                    Array.Fill(x, dx); Array.Fill(y, dy); Array.Fill(z, dz);
                    EngineAPI.EntityAPI.SetRotation(componentIds, x, y, z, count, _isLocalRotation ? (int)Space.Local : (int)Space.World);
                }
                return true;
        }
        return false;
    }

    protected override bool UpdateMSComponent()
    {
        ResetLocalFrame();

        if (SelectedComponents.Count == 1)
        {
            var c = SelectedComponents[0];
            PosX = c.Position.X; PosY = c.Position.Y; PosZ = c.Position.Z;
            RotX = c.Rotation.X; RotY = c.Rotation.Y; RotZ = c.Rotation.Z;
            ScaleX = c.Scale.X; ScaleY = c.Scale.Y; ScaleZ = c.Scale.Z;

            return true;
        }

        PosX = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Position.X));
        PosY = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Position.Y));
        PosZ = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Position.Z));

        RotX = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Rotation.X));
        RotY = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Rotation.Y));
        RotZ = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Rotation.Z));

        ScaleX = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Scale.X));
        ScaleY = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Scale.Y));
        ScaleZ = MSEntity.GetMixedValue(SelectedComponents, new Func<Transform, float>(x => x.Scale.Z));

        return true;
    }

    public MSTransform(MSEntity msEntity) : base(msEntity)
    {
        Refresh();
    }
}
