// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;

namespace PrimalEditor.Components;

enum CameraType
{
    Perspective,
    Orthographic,
}

[DataContract]
abstract class Camera(Scene scene, CameraType type) : GameEntity(scene)
{
    [DataMember]
    public CameraType Type { get; init; } = type;

    public IdType CameraId { get; private set; } = ID.INVALID_ID;

    public override bool IsActive
    {
        get => base.IsActive;
        set
        {
            if (base.IsActive != value)
            {
                if (value)
                {
                    base.IsActive = value;
                    Debug.Assert(!ID.IsValid(CameraId) && ID.IsValid(EntityId));
                    CameraId = EngineAPI.CreateCamera(this);
                    Debug.Assert(ID.IsValid(CameraId));
                }
                else if (ID.IsValid(CameraId))
                {
                    EngineAPI.RemoveCamera(CameraId);
                    CameraId = ID.INVALID_ID;
                    base.IsActive = value;
                }
            }
        }
    }        

    [DataMember]
    public float NearZ
    {
        get;
        set
        {
            value = float.Clamp(value, 0f, FarZ);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(NearZ));
            }
        }
    } = 0.1f;

    [DataMember]
    public float FarZ
    {
        get;
        set
        {
            value = float.Max(value, NearZ);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(FarZ));
            }
        }
    } = 100f;
}

[DataContract]
class PerspectiveCamera(Scene scene) : Camera(scene, CameraType.Perspective)
{
    [DataMember]
    public float FieldOfView
    {
        get;
        set
        {
            value = float.Clamp(value, 0.01f, 180f);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(FieldOfView));
            }
        }
    } = 45f;
}


[DataContract]
class OrthographicCamera(Scene scene) : Camera(scene, CameraType.Orthographic)
{
    [DataMember]
    public float OrthographicSize
    {
        get;
        set
        {
            value = float.Max(value, 0.01f);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(OrthographicSize));
            }
        }
    } = 4f;
}

abstract class MSCamera<T> : MSEntity where T : Camera
{
    public List<T> SelectedCameras { get; }

    public CameraType? CameraType
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CameraType));
            }
        }
    }

    public float? NearZ
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Clamp(value.Value, 0f, FarZ ?? SelectedCameras.Min(x => x.FarZ));
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(NearZ));
            }
        }
    }

    public float? FarZ
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Max(value.Value, NearZ ?? SelectedCameras.Max(x => x.NearZ));
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(FarZ));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        var count = SelectedCameras.Count;
        var ids = SelectedCameras.Select(x => x.CameraId).ToArray();
        var index = 0;

        switch (propertyName)
        {
            case nameof(NearZ):
                {
                    var nearZ = new float[count]; var farZ = new float[count];
                    SelectedCameras.ForEach(x =>
                    {
                        x.NearZ = NearZ.Value;
                        nearZ[index] = x.NearZ; farZ[index] = x.FarZ; ++index;
                    });
                    EngineAPI.SetCameraRange(ids, nearZ, farZ, count);
                    return true;
                }
            case nameof(FarZ):
                {
                    var nearZ = new float[count]; var farZ = new float[count];
                    SelectedCameras.ForEach(x =>
                    {
                        x.FarZ = FarZ.Value;
                        nearZ[index] = x.NearZ; farZ[index] = x.FarZ; ++index;
                    });
                    EngineAPI.SetCameraRange(ids, nearZ, farZ, count);
                    return true;
                }            
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        if (SelectedCameras.Count == 1)
        {
            var camera = SelectedCameras[0];
            NearZ = camera.NearZ;
            FarZ = camera.FarZ;
            CameraType = camera.Type;
        }
        else
        {            
            NearZ = GetMixedValue(SelectedCameras, new Func<Camera, float>(x => x.NearZ));
            FarZ = GetMixedValue(SelectedCameras, new Func<Camera, float>(x => x.FarZ));
            CameraType = (CameraType?)GetMixedValue(SelectedCameras, new Func<Camera, int>(x => (int)x.Type));
        }

        return base.UpdateMSGameEntity();
    }

    protected MSCamera(List<GameEntity> entities) : base(entities)
    {
        SelectedCameras = [.. entities.OfType<T>()];
        Refresh();
    }
}

class MSCamera(List<GameEntity> entities) : MSCamera<Camera>(entities) { }

class MSPerspectiveCamera(List<GameEntity> entities) : MSCamera<PerspectiveCamera>(entities)
{
    public float? FieldOfView
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Clamp(value.Value, 0.01f, 180f);
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(FieldOfView));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(FieldOfView):
                {
                    var count = SelectedCameras.Count;
                    var ids = SelectedCameras.Select(x => x.CameraId).ToArray();
                    var fov = new float[count];
                    var index = 0;
                    SelectedCameras.ForEach(x =>
                    {
                        x.FieldOfView = FieldOfView.Value;
                        fov[index++] = x.FieldOfView;
                    });
                    EngineAPI.SetCameraFieldOfView(ids, fov, count);
                    return true;
                }
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        FieldOfView = SelectedCameras.Count == 1 ?
            SelectedCameras[0].FieldOfView :
            GetMixedValue(SelectedCameras, new Func<PerspectiveCamera, float>(x => x.FieldOfView));

        return base.UpdateMSGameEntity();
    }
}

class MSOrthographicCamera(List<GameEntity> entities) : MSCamera<OrthographicCamera>(entities)
{
    public float? OrthographicSize
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Max(value.Value, 0f);
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(OrthographicSize));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        switch (propertyName)
        {
            case nameof(OrthographicSize):
                {
                    var count = SelectedCameras.Count;
                    var ids = SelectedCameras.Select(x => x.CameraId).ToArray();
                    var size = new float[count];
                    var index = 0;
                    SelectedCameras.ForEach(x =>
                    {
                        x.OrthographicSize = OrthographicSize.Value;
                        size[index++] = x.OrthographicSize;
                    });
                    EngineAPI.SetCameraOrthographicSize(ids, size, count);
                    return true;
                }
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        OrthographicSize = SelectedCameras.Count == 1 ?
            SelectedCameras[0].OrthographicSize :
            GetMixedValue(SelectedCameras, new Func<OrthographicCamera, float>(x => x.OrthographicSize));

        return base.UpdateMSGameEntity();
    }
}