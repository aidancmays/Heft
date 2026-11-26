using UnityEngine;

namespace Heft
{
    /// <summary>The force a grip applies. Pure functions, so they can be tested without physics.</summary>
    public static class CarryMath
    {
        /// <summary>
        /// The force one grip applies at its grab point: a spring-damper pulling the grab point toward the hold
        /// point, plus this grip's share of holding the object up against gravity, capped at the holder's max force.
        /// A holder too weak for its share can't hold the object up, so it sags and drags along the ground.
        /// </summary>
        /// <param name="error">Hold point minus grab point (world).</param>
        /// <param name="pointVelocity">The grab point's velocity minus the holder's own velocity (world).</param>
        /// <param name="mass">The object's mass.</param>
        /// <param name="share">This grip's share of the object (1 / number of grips).</param>
        /// <param name="gravity">Physics.gravity, when the object uses gravity; else zero.</param>
        public static Vector3 GripForce(Vector3 error, Vector3 pointVelocity, float mass, float share, Vector3 gravity,
            float stiffness, float damping, float maxForce)
        {
            Vector3 acceleration = error * stiffness - pointVelocity * damping;
            Vector3 force = (acceleration - gravity) * (mass * share);
            return Vector3.ClampMagnitude(force, Mathf.Max(0f, maxForce));
        }

        /// <summary>
        /// How hard holding is for one holder: the weight of their share against their max force. Below 1 they can
        /// hold it up; at 1 or more it is too heavy for them and drags. 0 with nothing to hold up.
        /// </summary>
        public static float Load(float mass, float share, float gravity, float maxForce)
        {
            float weight = mass * share * Mathf.Abs(gravity);
            if (weight <= 0f) return 0f;
            return maxForce <= 0f ? float.PositiveInfinity : weight / maxForce;
        }

        /// <summary>
        /// How much of a grip's pull acts at the grab point (1) rather than at the object's centre of mass (0).
        /// One holder lifting something light pulls it by the middle: pulled by an end, a small light object (a
        /// bottle by its neck) has so little inertia that the pull's torque and the turning fight each other and it
        /// spins and jitters. Heavier loads move toward the grab point, so what is too heavy pivots and drags from
        /// where it is held; with several holders each pulls at their own point, so they can tip it between them.
        /// </summary>
        /// <param name="load">The holder's <see cref="Load"/> for their share.</param>
        /// <param name="holders">How many hold it.</param>
        /// <param name="lightLoad">At or below this load the pull is all at the centre of mass.</param>
        /// <param name="heavyLoad">At or above this load it is all at the grab point.</param>
        public static float Leverage(float load, int holders, float lightLoad = 0.5f, float heavyLoad = 1f)
        {
            if (holders > 1) return 1f;
            if (heavyLoad <= lightLoad) return load >= heavyLoad ? 1f : 0f;
            return Mathf.Clamp01((load - lightLoad) / (heavyLoad - lightLoad));
        }

        /// <summary>
        /// A holder's turn of what they hold, turned further: <paramref name="yaw"/> degrees about their up axis and
        /// <paramref name="pitch"/> degrees about their right axis (positive tips the far side up), both in their
        /// facing's frame, so "left" and "up" stay left and up on screen however it is already turned. A default
        /// (all zero) quaternion counts as no turn.
        /// </summary>
        public static Quaternion TurnHold(Quaternion current, float yaw, float pitch)
        {
            current = Normalized(current);
            Quaternion turned = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.right) * current;
            return Normalized(turned);
        }

        /// <summary>
        /// The rotation a holder wants the object at: their (yaw-only) facing, then their turn, then the facing the
        /// object had relative to them when grabbed.
        /// </summary>
        public static Quaternion HoldTarget(Quaternion facing, Quaternion turn, Quaternion relative) =>
            facing * Normalized(turn) * relative;

        /// <summary>
        /// How close a grip is to slipping, 0..1, for showing it. A grip slips once its grab point has been more than
        /// <paramref name="breakDistance"/> from the hold point for <paramref name="slipTime"/>: up to the break
        /// distance strain rises with the stretch to <paramref name="atBreak"/>, then the rest of the way as the slip
        /// timer runs out. 1 = it is letting go.
        /// </summary>
        public static float Strain(float stretch, float breakDistance, float slipTimer, float slipTime, float atBreak = 0.8f)
        {
            atBreak = Mathf.Clamp01(atBreak);
            if (breakDistance <= 0f) return 1f;
            if (stretch <= breakDistance) return atBreak * Mathf.Clamp01(stretch / breakDistance);
            float slipping = slipTime <= 0f ? 1f : Mathf.Clamp01(slipTimer / slipTime);
            return atBreak + (1f - atBreak) * slipping;
        }

        /// <summary>A point along a quadratic curve from a through control (pulled toward) to b; t 0..1.</summary>
        public static Vector3 Curve(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * control + t * t * b;
        }

        /// <summary>
        /// The control point of a tether from <paramref name="from"/> to <paramref name="to"/> (the grab point): bent
        /// toward <paramref name="pull"/> (where the holder is pulling it, the hold point) by <paramref name="bend"/>
        /// (0..1) and sagging by <paramref name="sag"/> of its length when slack, straightening as strain rises.
        /// </summary>
        public static Vector3 TetherControl(Vector3 from, Vector3 to, Vector3 pull, float bend, float sag, float strain)
        {
            Vector3 middle = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);
            Vector3 toward = Vector3.Lerp(middle, pull, Mathf.Clamp01(bend));
            return toward + Vector3.down * (sag * length * (1f - Mathf.Clamp01(strain)));
        }

        /// <summary>The quaternion made unit length; a zero (default) one becomes identity.</summary>
        public static Quaternion Normalized(Quaternion q)
        {
            float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (length < 1e-6f) return Quaternion.identity;
            return new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }

        /// <summary>
        /// The speed change a throw gives an object: impulse over mass, never more than maxSpeed. Mass Floor is added to
        /// the mass so very light things don't fly off at the cap.
        /// </summary>
        public static float ThrowSpeed(float impulse, float mass, float maxSpeed, float massFloor = 0f) =>
            mass <= 0f && massFloor <= 0f ? maxSpeed : Mathf.Min(maxSpeed, impulse / Mathf.Max(1e-4f, mass + massFloor));
    }
}
