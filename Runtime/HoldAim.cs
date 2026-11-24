using System;
using Unity.Netcode;
using UnityEngine;

namespace Heft
{
    /// <summary>Where a holder wants their grip: the hold point, how fast it is moving, and which way they face.</summary>
    [Serializable]
    public struct HoldAim : INetworkSerializeByMemcpy, IEquatable<HoldAim>
    {
        public Vector3 Point;
        public Vector3 Velocity;
        public Vector3 Forward;

        public bool Equals(HoldAim other) => Point == other.Point && Velocity == other.Velocity && Forward == other.Forward;
        public override bool Equals(object obj) => obj is HoldAim other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Point, Velocity, Forward);
    }
}
