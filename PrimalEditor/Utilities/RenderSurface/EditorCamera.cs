// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using System;
using System.Numerics;

namespace PrimalEditor.Utilities;

class EditorCamera
{
    private static readonly float _minNearZ = 0.001f; // 1 mm
    private static readonly float _minDiffNearZFarZ = 0.001f;
    private static readonly float _minFov = 0.01f;
    private static readonly float _maxFov = 180f;

    private int _surfaceId = -1;
    private bool _updatePosition;
    private bool _updateRotation;
    private float _acceleration = 0f;
    private Vector3 _position;
    private Vector3 _rotation;
    private Vector3 _target;
    private Vector3 _desiredPosition;
    private Vector3 _desiredRotation;

    public float OrbitRadius { get; private set; }

    public float Speed
    {
        get;
        set
        {
            value = Math.Clamp(value, 1f, 10f);
            if (!field.IsTheSameAs(value))
            {
                field = value;
            }
        }
    } = 5f;

    public float FoV
    {
        get;
        set
        {
            value = Math.Clamp(value, _minFov, _maxFov);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                EngineAPI.SetCameraFoV(_surfaceId, field);
            }
        }
    } = 45f;

    public float NearZ
    {
        get;
        set
        {
            value = Math.Clamp(value, _minNearZ, FarZ - _minDiffNearZFarZ);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                EngineAPI.SetCameraRange(_surfaceId, field, FarZ);
            }
        }
    } = 0.1f;

    public float FarZ
    {
        get;
        set
        {
            value = Math.Max(value, NearZ + _minDiffNearZFarZ);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                EngineAPI.SetCameraRange(_surfaceId, NearZ, field);
            }
        }
    } = 100f;

    public Vector3 Target => _target;

    public void Goto(Vector3 desiredPosition)
    {
        _target = desiredPosition;
        Orbit(0, 0, 0, true);
    }

    private void Orbit(double dx, double dy, int dz, bool slide)
    {
        var theta = _desiredRotation.X + (float)dy * 0.005f;
        theta = Math.Clamp(theta, 0.0001f - MathUtil.HalfPi, MathUtil.HalfPi - 0.0001f);
        var phi = _desiredRotation.Y - (float)dx * 0.005f;
        OrbitRadius *= 1f - (0.1f * dz); // dz is either -1, 0 or +1.

        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(phi, theta, 0);
        var v = Vector3.TransformNormal(new(0, 0, 1), rotationMatrix);
        v = Vector3.Normalize(v);
        v *= OrbitRadius;

        _desiredPosition = _target - v;
        _desiredRotation.X = theta;
        _desiredRotation.Y = phi;

        if (!slide)
        {
            _position = _desiredPosition;
            _rotation = _desiredRotation;

            EngineAPI.UpdateEditorCamera(_surfaceId, _position, _rotation);
        }
        else
        {
            _updatePosition = true;
            _updateRotation = true;
        }
    }

    public void Orbit(double dx, double dy, int dz) => Orbit(dx, dy, dz, true);

    public void ChangePosition(Vector3 direction, float dt)
    {
        if (direction.LengthSquared() <= MathUtil.Epsilon) return;

        var theta = _desiredRotation.X;
        var phi = _desiredRotation.Y;

        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(phi, theta, 0);

        var dtScale = dt * 60;

        if (_acceleration < 1f)
        {
            _acceleration += 0.02f * dtScale;
        }
        var step = Speed * dtScale * (KeyboardHelper.GetAsyncKeyState(KeyboardHelper.VKey.Shift) < 0 ? 0.1f : 0.01f);
        var v = Vector3.Transform(direction * step, rotationMatrix) * _acceleration;

        _desiredPosition += v;
        _target += v;
        _updatePosition = true;
    }

    public void ChangeDirection(double dx, double dy)
    {
        var theta = _desiredRotation.X + (float)dy * 0.005f;
        theta = Math.Clamp(theta, 0.0001f - MathUtil.HalfPi, MathUtil.HalfPi - 0.0001f);
        var phi = _desiredRotation.Y - (float)dx * 0.005f;

        var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(phi, theta, 0);
        var v = Vector3.TransformNormal(new(0, 0, 1), rotationMatrix);
        v = Vector3.Normalize(v);
        v *= OrbitRadius;

        _target = _desiredPosition + v;
        _desiredRotation.X = theta;
        _desiredRotation.Y = phi;

        _updateRotation = true;
    }

    private void Seek(float dt)
    {
        var dtScale = 0.2f * dt * 60f;

        if (_updatePosition)
        {
            var p = _desiredPosition - _position;
            _updatePosition = p.LengthSquared() > 1e-8f;

            if (_updatePosition)
            {
                _position += p * dtScale;
            }
            else
            {
                _position = _desiredPosition;
                _acceleration = 0f;
            }
        }

        if (_updateRotation)
        {
            var o = _desiredRotation - _rotation;
            _updateRotation = o.LengthSquared() > 1e-8f;
            _rotation = _updateRotation ? _rotation + o * dtScale : _desiredRotation;
        }

        EngineAPI.UpdateEditorCamera(_surfaceId, _position, _rotation);
    }

    public void SetSurfaceId(int surfaceId)
    {
        _surfaceId = surfaceId;
        EngineAPI.UpdateEditorCamera(_surfaceId, _position, _rotation);
    }

    public void Update(float dt)
    {
        if ((_updatePosition || _updateRotation) && ID.IsValid(_surfaceId))
        {
            Seek(dt);
        }
    }

    public EditorCamera()
    {
        OrbitRadius = 3f;
        _position = _desiredPosition = new(0, 1, 10);
        _rotation = _desiredRotation = new(0, -MathUtil.Pi, 0);
        _target = new(0, 1, _position.Z - OrbitRadius);
    }
}