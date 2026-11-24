using System;
using System.Collections.Generic;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// Wheels for a rigidbody: a cart, a barrow, a trolley. Where it touches the ground it rolls along its forward
    /// (blue) axis almost freely and grips hard sideways, so it is far easier to push the way it rolls than across.
    /// Fixed wheels grip sideways; swivelling wheels (casters) only roll, so the end with the casters swings round when
    /// pushed. Wheels spin, and casters turn to trail, on every machine, from how the object moves.
    /// </summary>
    /// <remarks>
    /// The rolling forces run wherever the body is simulated (the server, or offline: wherever it isn't kinematic).
    /// Give each wheel a small collider of its own (a sphere) as its contact; those colliders are made frictionless
    /// when it starts, so only this component decides how it rolls. For a pushed cart, set the Carryable's Lift to 0
    /// and Turn Yaw Only on.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Rolling : MonoBehaviour
    {
        [Serializable]
        public class Wheel
        {
            [Tooltip("The wheel's centre. Spun about its parent's right (red) axis to show it rolling.")]
            public Transform wheel;
            [Min(0.01f)] public float radius = 0.18f;
            [Tooltip("A caster: rolls any way, no sideways grip, turns to trail the way it moves.")]
            public bool swivels;
            [Tooltip("This wheel's ground contact, made frictionless when it starts (so it doesn't drag).")]
            public Collider contact;

            [NonSerialized] public Quaternion rest;
            [NonSerialized] public Vector3 lastPosition;
            [NonSerialized] public float spin;
            [NonSerialized] public float yaw;
            [NonSerialized] public bool grounded;
        }

        [SerializeField] List<Wheel> wheels = new List<Wheel>();

        [Header("Rolling")]
        [Tooltip("How strongly fixed wheels stop it sliding sideways: 1 cancels all sideways slide each physics step.")]
        [SerializeField, Range(0f, 1f)] float sideGrip = 0.9f;
        [Tooltip("Most sideways force a fixed wheel can hold, as a share of the weight on it (like tyre friction). Shoved harder than this it skids.")]
        [SerializeField, Min(0f)] float sideFriction = 1.2f;
        [Tooltip("How much the wheels resist rolling, as a share of the weight on them. Small = it coasts.")]
        [SerializeField, Min(0f)] float rollingResistance = 0.03f;
        [Tooltip("Push the sideways grip at the height of the centre of mass, so turning hard doesn't tip it over.")]
        [SerializeField] bool antiRoll = true;
        [Tooltip("How far below a wheel's bottom it still counts as on the ground.")]
        [SerializeField, Min(0f)] float groundSnap = 0.06f;
        [SerializeField] LayerMask ground = ~0;

        [Header("Balance")]
        [Tooltip("Use Centre Of Mass below instead of the one Unity works out from the colliders (lower = harder to tip).")]
        [SerializeField] bool overrideCentreOfMass;
        [SerializeField] Vector3 centreOfMass;

        [Header("Look")]
        [Tooltip("How quickly casters turn to trail the way they move (per second).")]
        [SerializeField, Min(0f)] float swivelSpeed = 8f;

        Rigidbody body;
        static PhysicsMaterial frictionless;

        public Rigidbody Body => body;
        public IReadOnlyList<Wheel> Wheels => wheels;

        /// <summary>How many wheels touch the ground (where it is simulated).</summary>
        public int GroundedWheels { get; private set; }

        /// <summary>Speed along its forward axis (every machine, from how it moves).</summary>
        public float ForwardSpeed { get; private set; }

        Vector3 lastRootPosition;

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            if (overrideCentreOfMass) body.centerOfMass = centreOfMass;

            if (frictionless == null)
            {
                frictionless = new PhysicsMaterial("Heft Wheel")
                {
                    dynamicFriction = 0f,
                    staticFriction = 0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounciness = 0f,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            foreach (Wheel w in wheels)
            {
                if (w == null) continue;
                if (w.contact != null) w.contact.sharedMaterial = frictionless;
                if (w.wheel != null)
                {
                    w.rest = w.wheel.localRotation;
                    w.lastPosition = w.wheel.position;
                }
            }
            lastRootPosition = transform.position;
        }

        /// <summary>
        /// How hard it is to push the way of <paramref name="direction"/> (world space), as a share of its weight:
        /// Rolling Resistance straight along the way it rolls, rising to Side Friction straight across (if it has
        /// fixed wheels; on casters alone it rolls any way). Zero direction counts as along.
        /// </summary>
        public float PushResistance(Vector3 direction)
        {
            Vector3 up = transform.up;
            Vector3 flat = Vector3.ProjectOnPlane(direction, up);
            if (flat.sqrMagnitude < 1e-6f) return rollingResistance;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            float across = HasFixedWheels ? sideFriction : rollingResistance;
            return RollMath.PushResistance(Vector3.Dot(flat.normalized, forward), rollingResistance, across);
        }

        bool HasFixedWheels
        {
            get
            {
                foreach (Wheel w in wheels)
                    if (w != null && !w.swivels) return true;
                return false;
            }
        }

        /// <summary>For tools that build wheeled prefabs.</summary>
        public void AddWheel(Transform wheel, float radius, bool swivels, Collider contact)
        {
            wheels.Add(new Wheel { wheel = wheel, radius = radius, swivels = swivels, contact = contact });
        }

        /// <summary>For tools: the centre of mass to use (local space).</summary>
        public void SetCentreOfMass(Vector3 local)
        {
            overrideCentreOfMass = true;
            centreOfMass = local;
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic) return;

            int grounded = 0;
            foreach (Wheel w in wheels)
            {
                w.grounded = false;
                if (w == null || w.wheel == null) continue;
                if (Physics.Raycast(w.wheel.position, -transform.up, out RaycastHit hit, w.radius + groundSnap, ground,
                        QueryTriggerInteraction.Ignore) && hit.rigidbody != body)
                {
                    w.grounded = true;
                    grounded++;
                }
            }
            GroundedWheels = grounded;
            if (grounded == 0) return;

            float dt = Time.fixedDeltaTime;
            float g = Physics.gravity.magnitude;
            float massShare = body.mass / grounded;
            float weightShare = massShare * g;
            Vector3 up = transform.up;
            Vector3 centre = body.worldCenterOfMass;

            foreach (Wheel w in wheels)
            {
                if (w == null || !w.grounded) continue;
                Vector3 contact = w.wheel.position - up * w.radius;
                Vector3 velocity = body.GetPointVelocity(contact);
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
                Vector3 side = Vector3.Cross(up, forward);

                Vector3 force = forward * RollMath.RollingForce(Vector3.Dot(velocity, forward), massShare, weightShare,
                    rollingResistance, dt);
                if (!w.swivels)
                    force += side * RollMath.SideForce(Vector3.Dot(velocity, side), massShare, weightShare, sideGrip,
                        sideFriction, dt);

                Vector3 at = antiRoll ? contact + up * Vector3.Dot(centre - contact, up) : contact;
                body.AddForceAtPosition(force, at, ForceMode.Force);
            }
        }

        // Every machine: spin the wheels and swing the casters from how they actually moved.
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 forward = transform.forward;
            ForwardSpeed = Vector3.Dot(transform.position - lastRootPosition, forward) / dt;
            lastRootPosition = transform.position;

            foreach (Wheel w in wheels)
            {
                if (w == null || w.wheel == null) continue;
                Vector3 moved = w.wheel.position - w.lastPosition;
                w.lastPosition = w.wheel.position;
                Transform parent = w.wheel.parent != null ? w.wheel.parent : transform;
                Vector3 local = parent.InverseTransformDirection(moved);
                local.y = 0f;

                float along;
                if (w.swivels)
                {
                    if (local.magnitude > 0.0005f)
                    {
                        float target = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                        // A caster rolling backwards trails the other way; take whichever way is nearer.
                        float back = Mathf.DeltaAngle(w.yaw, target + 180f);
                        float ahead = Mathf.DeltaAngle(w.yaw, target);
                        float turn = Mathf.Abs(back) < Mathf.Abs(ahead) ? back : ahead;
                        w.yaw += turn * Mathf.Clamp01(swivelSpeed * dt);
                    }
                    Vector3 heading = Quaternion.Euler(0f, w.yaw, 0f) * Vector3.forward;
                    along = Vector3.Dot(local, heading);
                }
                else along = local.z;

                w.spin = Mathf.Repeat(w.spin + along / w.radius * Mathf.Rad2Deg, 360f);
                w.wheel.localRotation = Quaternion.Euler(0f, w.yaw, 0f) * Quaternion.AngleAxis(w.spin, Vector3.right) * w.rest;
            }
        }

        void OnDrawGizmosSelected()
        {
            foreach (Wheel w in wheels)
            {
                if (w == null || w.wheel == null) continue;
                Gizmos.color = w.swivels ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.9f, 1f);
                Gizmos.DrawWireSphere(w.wheel.position, w.radius);
                if (!w.swivels)
                {
                    Vector3 right = transform.right * 0.25f;
                    Gizmos.DrawLine(w.wheel.position - right, w.wheel.position + right);
                }
            }
            if (overrideCentreOfMass)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(transform.TransformPoint(centreOfMass), 0.05f);
            }
        }
    }

    /// <summary>Wheel forces. Pure functions, so they can be tested without physics.</summary>
    public static class RollMath
    {
        /// <summary>
        /// The sideways force a fixed wheel puts on its share of the body to stop it sliding across: enough to cancel
        /// <paramref name="grip"/> of the slide in one step, never more than <paramref name="friction"/> times the
        /// weight on the wheel (past that it skids).
        /// </summary>
        /// <param name="sideSpeed">The contact's speed across the wheel (m/s, signed).</param>
        /// <param name="massShare">The body's mass over the wheels on the ground.</param>
        /// <param name="weightShare">The weight on this wheel (N).</param>
        public static float SideForce(float sideSpeed, float massShare, float weightShare, float grip, float friction, float dt)
        {
            if (dt <= 0f) return 0f;
            float wanted = -sideSpeed * massShare * Mathf.Clamp01(grip) / dt;
            float most = Mathf.Max(0f, friction) * weightShare;
            return Mathf.Clamp(wanted, -most, most);
        }

        /// <summary>
        /// The force against rolling: <paramref name="resistance"/> times the weight on the wheel, against the way it
        /// rolls, but never more than would stop it this step (so it doesn't rock back and forth when still).
        /// </summary>
        public static float RollingForce(float forwardSpeed, float massShare, float weightShare, float resistance, float dt)
        {
            if (dt <= 0f || forwardSpeed == 0f) return 0f;
            float most = Mathf.Max(0f, resistance) * weightShare;
            float stop = Mathf.Abs(forwardSpeed) * massShare / dt;
            return -Mathf.Sign(forwardSpeed) * Mathf.Min(most, stop);
        }

        /// <summary>
        /// How hard pushing is at an angle, as a share of the weight: <paramref name="rolling"/> straight along
        /// (<paramref name="along"/> = ±1, the cosine of the angle to the way it rolls), <paramref name="side"/>
        /// straight across (0). It eases toward Side as the push turns from the rolling way (by the square of the
        /// cosine), so a slightly crooked push stays easy and a push across is hard.
        /// </summary>
        public static float PushResistance(float along, float rolling, float side)
        {
            float a = Mathf.Clamp(along, -1f, 1f);
            return Mathf.Lerp(Mathf.Max(0f, side), Mathf.Max(0f, rolling), a * a);
        }

        /// <summary>How much harder pushing across is than pushing along: the most side force over rolling resistance.</summary>
        public static float Anisotropy(float sideFriction, float rollingResistance) =>
            rollingResistance <= 0f ? float.PositiveInfinity : sideFriction / rollingResistance;
    }
}
