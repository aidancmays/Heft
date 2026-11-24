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

        /// <summary>The speed change a throw gives an object: impulse over mass, never more than maxSpeed.</summary>
        public static float ThrowSpeed(float impulse, float mass, float maxSpeed) =>
            mass <= 0f ? maxSpeed : Mathf.Min(maxSpeed, impulse / mass);
    }
}
