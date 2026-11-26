using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// A physics object players can grab and carry. The server simulates it: each holder's Grabber sends where they
    /// want it (their hold point), and every grip pulls its grab point there with a spring whose force is capped by
    /// that holder's strength. Light things follow the hand, heavy things sag and drag, and several holders share the
    /// weight. Sync the body with a NetworkTransform in Server authority (and a NetworkRigidbody).
    ///
    /// Despawning a held object lets every holder go, on the server and on clients.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Carryable : NetworkBehaviour, IHoldable
    {
        [Tooltip("Most players that can hold it at once.")]
        [SerializeField, Min(1)] int maxHolders = 4;

        [Header("Grip")]
        [Tooltip("How hard each grip pulls its grab point toward the hold point, per metre (as acceleration).")]
        [SerializeField, Min(0f)] float stiffness = 150f;
        [Tooltip("Damps the grab point's speed relative to the holder, so it settles instead of bouncing.")]
        [SerializeField, Min(0f)] float damping = 20f;
        [Tooltip("If the grab point stays this far from the hold point for Slip Time, the grip slips.")]
        [SerializeField, Min(0.1f)] float breakDistance = 1.75f;
        [SerializeField, Min(0f)] float slipTime = 0.3f;

        [Header("Turning")]
        [Tooltip("Turns the object with the first holder, keeping the facing it had when grabbed. 0 = let it swing freely.")]
        [SerializeField, Min(0f)] float turnStrength = 60f;
        [SerializeField, Min(0f)] float turnDamping = 12f;
        [Tooltip("Angular damping while held, so it doesn't spin in the hand.")]
        [SerializeField, Min(0f)] float heldAngularDamping = 5f;
        [Tooltip("Turn it only about the vertical, so it stays upright on its own wheels or base (a cart, a barrow).")]
        [SerializeField] bool turnYawOnly;

        [Header("Ground")]
        [Tooltip("How much of its weight holders take. 1 = they lift it bodily. 0 = it stays on the ground (it rolls or slides) and grips only push and pull it along the ground, never up.")]
        [SerializeField, Range(0f, 1f)] float lift = 1f;
        [Tooltip("While it isn't lifted, how heavy moving it feels, as a share of its weight (for Load: how much it slows the holder). With Rolling on it the wheels decide instead: light the way it rolls, heavy across.")]
        [SerializeField, Range(0f, 1f)] float pushEffort = 0.15f;

        [Header("Loose")]
        [Tooltip("Damping while nobody holds it, so dropped and thrown things settle instead of rolling and spinning forever.")]
        [SerializeField, Min(0f)] float looseLinearDamping = 0.15f;
        [SerializeField, Min(0f)] float looseAngularDamping = 1.5f;

        readonly NetworkVariable<byte> holderCount = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        struct Grip
        {
            public Grabber Grabber;
            public Vector3 LocalPoint;
            public Quaternion Relative; // rotation relative to the holder's facing when grabbed
            public float SlipTimer;
        }

        readonly List<Grip> grips = new List<Grip>();
        Rigidbody body;
        Collider[] colliders;
        Rolling rolling;
        float restAngularDamping;

        /// <summary>True while at least one player holds it (synced).</summary>
        public bool IsHeld => holderCount.Value > 0;

        /// <summary>How many players hold it (synced).</summary>
        public int HolderCount => holderCount.Value;

        public int MaxHolders => maxHolders;
        public Rigidbody Body => body;
        public float Mass => body.mass;

        /// <summary>How much of its weight holders take (1 = lifted, 0 = pushed along the ground).</summary>
        public float Lift { get => lift; set => lift = Mathf.Clamp01(value); }

        /// <summary>
        /// The mass holders feel: all of it when lifted, Push Effort of it when it stays on the ground (on wheels:
        /// pushed the way it rolls). See <see cref="LoadMassToward"/>.
        /// </summary>
        public float LoadMass => LoadMassToward(Vector3.zero);

        /// <summary>
        /// The mass holders feel moving it the way of <paramref name="direction"/> (world space; zero = the easiest
        /// way). Lifted: all of it. On the ground: Push Effort of it, or with <see cref="Rolling"/>, the wheels'
        /// resistance that way (tiny the way it rolls, heavy across). What <see cref="Grabber.Load"/> is measured
        /// against.
        /// </summary>
        public float LoadMassToward(Vector3 direction)
        {
            if (lift >= 1f) return body.mass;
            float effort = rolling != null ? rolling.PushResistance(direction) : pushEffort;
            return body.mass * (lift + (1f - lift) * effort);
        }

        /// <summary>The object's colliders (the holders' own colliders ignore them while holding).</summary>
        public IReadOnlyList<Collider> Colliders => colliders;

        /// <summary>IsHeld changed (every machine).</summary>
        public event Action<bool> HeldChanged;

        /// <summary>Server: a player took hold.</summary>
        public event Action<Grabber> Grabbed;

        /// <summary>Server: a player let go (released, threw, slipped, or the object despawned).</summary>
        public event Action<Grabber> Released;

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>(true);
            rolling = GetComponent<Rolling>();
            body.linearDamping = Mathf.Max(body.linearDamping, looseLinearDamping);
            body.angularDamping = Mathf.Max(body.angularDamping, looseAngularDamping);
            restAngularDamping = body.angularDamping;
        }

        public override void OnNetworkSpawn()
        {
            holderCount.OnValueChanged += OnHolderCountChanged;
        }

        public override void OnNetworkDespawn()
        {
            holderCount.OnValueChanged -= OnHolderCountChanged;
            if (IsServer) ReleaseAll();
            if (body != null) body.angularDamping = restAngularDamping;
        }

        void OnHolderCountChanged(byte previous, byte current)
        {
            if ((previous > 0) != (current > 0)) HeldChanged?.Invoke(current > 0);
        }

        /// <summary>Is this player one of the holders? Server only.</summary>
        public bool IsHeldBy(Grabber grabber) => IndexOf(grabber) >= 0;

        /// <summary>Server: makes every holder let go.</summary>
        public void ReleaseAll()
        {
            for (int i = grips.Count - 1; i >= 0; i--) RemoveGripAt(i);
        }

        /// <summary>Server: takes a grip at a point in the object's local space. False when it is full or already held by them.</summary>
        internal bool AddGrip(Grabber grabber, Vector3 localPoint, Vector3 forward)
        {
            if (!IsServer || grabber == null || grips.Count >= maxHolders || IndexOf(grabber) >= 0) return false;

            grips.Add(new Grip
            {
                Grabber = grabber,
                LocalPoint = localPoint,
                Relative = Quaternion.Inverse(Facing(forward)) * body.rotation,
            });
            body.angularDamping = Mathf.Max(restAngularDamping, heldAngularDamping);
            body.WakeUp();
            holderCount.Value = (byte)grips.Count;
            Grabbed?.Invoke(grabber);
            return true;
        }

        /// <summary>Server: lets one holder go.</summary>
        internal void RemoveGrip(Grabber grabber)
        {
            int index = IndexOf(grabber);
            if (index >= 0) RemoveGripAt(index);
        }

        /// <summary>Server: sends it flying along a direction (after the thrower has let go).</summary>
        internal void Throw(Vector3 direction, float impulse, float maxSpeed, float massFloor)
        {
            if (!IsServer || direction.sqrMagnitude < 1e-6f) return;
            body.AddForce(direction.normalized * CarryMath.ThrowSpeed(impulse, body.mass, maxSpeed, massFloor), ForceMode.VelocityChange);
        }

        void RemoveGripAt(int index)
        {
            Grabber grabber = grips[index].Grabber;
            grips.RemoveAt(index);
            if (grips.Count == 0) body.angularDamping = restAngularDamping;
            if (IsSpawned) holderCount.Value = (byte)grips.Count;
            if (grabber != null) grabber.ServerLetGo(this);
            Released?.Invoke(grabber);
        }

        int IndexOf(Grabber grabber)
        {
            for (int i = 0; i < grips.Count; i++)
                if (grips[i].Grabber == grabber) return i;
            return -1;
        }

        void FixedUpdate()
        {
            if (!IsServer || !IsSpawned || grips.Count == 0) return;

            float share = 1f / grips.Count;
            Vector3 gravity = body.useGravity ? Physics.gravity : Vector3.zero;
            float dt = Time.fixedDeltaTime;

            for (int i = grips.Count - 1; i >= 0; i--)
            {
                Grip grip = grips[i];
                Grabber grabber = grip.Grabber;
                if (grabber == null || !grabber.IsSpawned || !grabber.CanHold)
                {
                    RemoveGripAt(i);
                    continue;
                }

                HoldAim aim = grabber.ServerAim;
                Vector3 point = body.transform.TransformPoint(grip.LocalPoint);
                // What isn't lifted is only pushed along the ground: the up and down part of the pull goes.
                Vector3 error = Flatten(aim.Point - point);

                grip.SlipTimer = error.magnitude > breakDistance ? grip.SlipTimer + dt : 0f;
                if (grip.SlipTimer > slipTime)
                {
                    RemoveGripAt(i);
                    continue;
                }
                grips[i] = grip;
                grabber.ServerSetStrain(CarryMath.Strain(error.magnitude, breakDistance, grip.SlipTimer, slipTime));

                // Light things alone are pulled by the middle (no torque to fight the turning); heavy things, and
                // things held by several, from the grab point. The error is the same either way: moving the centre
                // by it moves the grab point by it too.
                // Something pushed along the ground is pushed where it is held, so pushing a corner steers it.
                float load = CarryMath.Load(LoadMass, share, gravity.magnitude, grabber.MaxForce);
                float lever = Mathf.Lerp(1f, CarryMath.Leverage(load, grips.Count), lift);
                Vector3 at = Vector3.Lerp(body.worldCenterOfMass, point, lever);
                Vector3 velocity = Vector3.Lerp(body.linearVelocity, body.GetPointVelocity(point), lever);
                Vector3 force = CarryMath.GripForce(error, Flatten(velocity - aim.Velocity), body.mass, share, gravity * lift,
                    stiffness, damping, grabber.MaxForce);
                body.AddForceAtPosition(force, at, ForceMode.Force);
            }

            if (grips.Count > 0 && turnStrength > 0f) Turn(grips[0], share, gravity);
        }

        void Turn(Grip grip, float share, Vector3 gravity)
        {
            // Weak holders can't swing heavy things around: scale the turn by how easily they hold their share.
            float load = CarryMath.Load(LoadMass, share, gravity.magnitude, grip.Grabber.MaxForce);
            float ease = load <= 0f ? 1f : Mathf.Clamp01(1f / load - 0.5f);
            if (ease <= 0f) return;

            HoldAim aim = grip.Grabber.ServerAim;
            Quaternion target = CarryMath.HoldTarget(Facing(aim.Forward), aim.Rotation, grip.Relative);
            Quaternion delta = target * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
            if (angle > 180f) angle -= 360f;

            Vector3 acceleration = axis * (angle * Mathf.Deg2Rad * turnStrength) - body.angularVelocity * turnDamping;
            if (turnYawOnly) acceleration = Vector3.Project(acceleration, Vector3.up);
            body.AddTorque(acceleration * ease, ForceMode.Acceleration);
        }

        /// <summary>A pull with its vertical part scaled by Lift (all of it kept when lifted, none when pushed).</summary>
        Vector3 Flatten(Vector3 v)
        {
            if (lift >= 1f) return v;
            v.y *= lift;
            return v;
        }

        /// <summary>The holder's facing as a yaw-only rotation (looking up or down doesn't tip what they carry).</summary>
        static Quaternion Facing(Vector3 forward)
        {
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
