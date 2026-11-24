using System;
using Unity.Netcode;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// Where a holder wants their grip: the hold point, how fast it is moving, which way they face, and how they have
    /// turned what they hold (relative to their facing; see <see cref="Grabber.Rotate"/>).
    /// </summary>
    [Serializable]
    public struct HoldAim : INetworkSerializeByMemcpy, IEquatable<HoldAim>
    {
        public Vector3 Point;
        public Vector3 Velocity;
        public Vector3 Forward;
        /// <summary>The holder's turn of the object, in their facing's frame. Default (all zero) = none.</summary>
        public Quaternion Rotation;

        public bool Equals(HoldAim other) => Point == other.Point && Velocity == other.Velocity && Forward == other.Forward
            && Rotation.Equals(other.Rotation);
        public override bool Equals(object obj) => obj is HoldAim other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Point, Velocity, Forward, Rotation);
    }
}
