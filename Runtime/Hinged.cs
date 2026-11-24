using System;
using Unity.Netcode;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// Hangs a networked rigidbody on a hinge: a book cover, a door, a lid, a trapdoor. The server simulates the hinge
    /// (clients watch the body through its NetworkTransform, like any Carryable), so players can swing it by grabbing
    /// it with a <see cref="Grabber"/>. The hinge can break when pulled hard enough, or be unbreakable.
    /// </summary>
    /// <remarks>
    /// The hinge is made where the object is when it spawns (or starts, offline): the anchor and axis are in the
    /// object's local space, and it hangs from <see cref="ConnectedBody"/>, or from the world when that is empty.
    /// Once broken it is an ordinary loose physics object. Put it next to a Carryable to let players swing it; a
    /// Carryable with Turn Strength 0 swings most naturally on a hinge.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class Hinged : NetworkBehaviour
    {
        [Tooltip("What it hangs from. Empty = fixed to the world where it spawns.")]
        [SerializeField] Rigidbody connectedBody;
        [Tooltip("The hinge's pivot, in this object's local space.")]
        [SerializeField] Vector3 anchor;
        [Tooltip("The axis it turns about, in this object's local space.")]
        [SerializeField] Vector3 axis = Vector3.up;

        [Header("Limits")]
        [SerializeField] bool useLimits;
        [Tooltip("Degrees from where it spawned, about the axis.")]
        [SerializeField, Range(-177f, 0f)] float minAngle = -90f;
        [SerializeField, Range(0f, 177f)] float maxAngle = 90f;
        [Tooltip("How much it bounces off a limit (0 = not at all).")]
        [SerializeField, Range(0f, 1f)] float limitBounce;

        [Header("Feel")]
        [Tooltip("Resists swinging, like stiff leather or a heavy old hinge. 0 = swings freely.")]
        [SerializeField, Min(0f)] float friction = 0.5f;
        [Tooltip("Pulls it back toward Rest Angle, like a sprung door. 0 = stays where it is left.")]
        [SerializeField, Min(0f)] float spring;
        [SerializeField] float restAngle;

        [Header("Breaking")]
        [Tooltip("Never breaks, however hard it is pulled.")]
        [SerializeField] bool unbreakable = true;
        [Tooltip("Force (N) on the hinge that tears it off.")]
        [SerializeField, Min(0f)] float breakForce = 600f;
        [Tooltip("Torque (N m) on the hinge that tears it off.")]
        [SerializeField, Min(0f)] float breakTorque = 300f;

        readonly NetworkVariable<bool> broken = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        HingeJoint joint;
        Rigidbody body;
        Quaternion restRotation;
        bool brokenLocal;

        /// <summary>True once the hinge has broken (synced).</summary>
        public bool IsBroken => IsSpawned ? broken.Value : brokenLocal;

        public bool Unbreakable
        {
            get => unbreakable;
            set { unbreakable = value; ApplyBreaking(); }
        }

        public float BreakForce
        {
            get => breakForce;
            set { breakForce = Mathf.Max(0f, value); ApplyBreaking(); }
        }

        public float BreakTorque
        {
            get => breakTorque;
            set { breakTorque = Mathf.Max(0f, value); ApplyBreaking(); }
        }

        /// <summary>The joint, where the hinge is simulated (the server, or offline), while unbroken. Null elsewhere.</summary>
        public HingeJoint Joint => joint;

        /// <summary>
        /// Degrees turned about the axis since it spawned, on every machine (worked out from the rotation, so clients
        /// get it too). Unreliable once broken.
        /// </summary>
        public float Angle
        {
            get
            {
                Quaternion delta = Quaternion.Inverse(restRotation) * transform.localRotation;
                return HingeMath.AngleAbout(delta, axis);
            }
        }

        /// <summary>The hinge broke (every machine).</summary>
        public event Action Broke;

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            restRotation = transform.localRotation;
        }

        void Start()
        {
            // Offline (no session): hinge it locally.
            var network = NetworkManager.Singleton;
            if (!IsSpawned && (network == null || !network.IsListening)) MakeJoint();
        }

        public override void OnNetworkSpawn()
        {
            restRotation = transform.localRotation;
            broken.OnValueChanged += OnBrokenChanged;
            if (IsServer && !broken.Value) MakeJoint();
        }

        public override void OnNetworkDespawn()
        {
            broken.OnValueChanged -= OnBrokenChanged;
            if (joint != null) Destroy(joint);
            joint = null;
        }

        void OnBrokenChanged(bool previous, bool current)
        {
            if (current && !previous && !IsServer) Broke?.Invoke();
        }

        /// <summary>Server (or offline): tears it off now, whatever its breaking settings.</summary>
        public void BreakNow()
        {
            if (IsSpawned && !IsServer) return;
            if (joint != null) Destroy(joint);
            joint = null;
            MarkBroken();
        }

        void MakeJoint()
        {
            if (joint != null || IsBroken) return;
            restRotation = transform.localRotation;
            joint = gameObject.AddComponent<HingeJoint>();
            joint.autoConfigureConnectedAnchor = true;
            joint.connectedBody = connectedBody;
            joint.anchor = anchor;
            joint.axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            joint.enableCollision = false;

            joint.useLimits = useLimits;
            if (useLimits)
            {
                joint.limits = new JointLimits { min = minAngle, max = maxAngle, bounciness = limitBounce };
            }

            // Friction as a damper with no pull; Spring pulls toward the rest angle.
            joint.useSpring = friction > 0f || spring > 0f;
            joint.spring = new JointSpring { spring = spring, damper = friction, targetPosition = restAngle };

            ApplyBreaking();
            body.WakeUp();
        }

        void ApplyBreaking()
        {
            if (joint == null) return;
            joint.breakForce = unbreakable ? Mathf.Infinity : breakForce;
            joint.breakTorque = unbreakable ? Mathf.Infinity : breakTorque;
        }

        // Unity: a joint on this object broke (it is destroyed after this).
        void OnJointBreak(float force)
        {
            joint = null;
            MarkBroken();
        }

        void MarkBroken()
        {
            if (IsBroken) return;
            brokenLocal = true;
            if (IsSpawned && IsServer) broken.Value = true;
            Broke?.Invoke();
        }

        /// <summary>For tools that build hinged prefabs.</summary>
        public void Setup(Vector3 localAnchor, Vector3 localAxis, bool canBreak, float force = 600f, float torque = 300f)
        {
            anchor = localAnchor;
            axis = localAxis;
            unbreakable = !canBreak;
            breakForce = force;
            breakTorque = torque;
        }

        /// <summary>For tools: limits in degrees from the spawn pose (useLimits false = none).</summary>
        public void SetLimits(bool use, float min, float max)
        {
            useLimits = use;
            minAngle = Mathf.Clamp(min, -177f, 0f);
            maxAngle = Mathf.Clamp(max, 0f, 177f);
        }

        /// <summary>For tools: how stiff the hinge feels.</summary>
        public void SetFeel(float hingeFriction, float hingeSpring = 0f, float hingeRestAngle = 0f)
        {
            friction = Mathf.Max(0f, hingeFriction);
            spring = Mathf.Max(0f, hingeSpring);
            restAngle = hingeRestAngle;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 pivot = transform.TransformPoint(anchor);
            Vector3 along = transform.TransformDirection(axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up);
            Gizmos.color = unbreakable ? new Color(0.4f, 0.9f, 1f) : new Color(1f, 0.55f, 0.3f);
            Gizmos.DrawLine(pivot - along * 0.5f, pivot + along * 0.5f);
            Gizmos.DrawWireSphere(pivot, 0.04f);
        }
    }

    /// <summary>Plain hinge maths, for <see cref="Hinged"/> and tests.</summary>
    public static class HingeMath
    {
        /// <summary>
        /// The signed angle (degrees, -180 to 180) a rotation turns about an axis, ignoring any twist about other
        /// axes (swing-twist decomposition).
        /// </summary>
        public static float AngleAbout(Quaternion rotation, Vector3 axis)
        {
            if (axis.sqrMagnitude < 1e-9f) return 0f;
            axis.Normalize();
            Vector3 v = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projected = Vector3.Dot(v, axis) * axis;
            var twist = new Quaternion(projected.x, projected.y, projected.z, rotation.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-9f) return 180f;
            twist.x /= magnitude; twist.y /= magnitude; twist.z /= magnitude; twist.w /= magnitude;

            float angle = 2f * Mathf.Atan2(new Vector3(twist.x, twist.y, twist.z).magnitude, twist.w) * Mathf.Rad2Deg;
            if (Vector3.Dot(new Vector3(twist.x, twist.y, twist.z), axis) < 0f) angle = -angle;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
