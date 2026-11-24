using System;
using System.Collections.Generic;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// The load space of a cart, a barrow or a moving platform: a box (local to this object) that keeps track of the
    /// rigidbodies in it and helps them ride along. Things put in move with the bed instead of sliding about as it
    /// speeds up, turns and stops; something tipped out or lifted clear leaves it. Held things still count as in the
    /// bed (their holder moves them, so the bed doesn't).
    /// </summary>
    /// <remarks>
    /// Put it on the object that carries the load (usually the root, next to the Rigidbody). Contents are found on
    /// every machine; the riding help only acts on bodies that are simulated here (not kinematic), so on a server it
    /// moves the networked loose items and on clients it does nothing.
    /// </remarks>
    [DisallowMultipleComponent]
    public class CargoBed : MonoBehaviour
    {
        [Tooltip("The load space's centre, in this object's local space.")]
        [SerializeField] Vector3 center = new Vector3(0f, 0.6f, 0f);
        [Tooltip("The load space's size, in this object's local space.")]
        [SerializeField] Vector3 size = new Vector3(0.8f, 0.4f, 1.1f);
        [SerializeField] LayerMask layers = ~0;

        [Header("Riding")]
        [Tooltip("How quickly things in the bed are brought to the bed's own speed along the ground (per second). 0 = only friction keeps them on.")]
        [SerializeField, Min(0f)] float hold = 6f;
        [Tooltip("Most speed change per second the riding help gives (so a hard stop can still spill things).")]
        [SerializeField, Min(0f)] float maxAcceleration = 12f;
        [Tooltip("Heaviest thing the bed helps along (kg). Heavier things just sit on it under friction.")]
        [SerializeField, Min(0f)] float maxMass = 80f;

        readonly List<Rigidbody> contents = new List<Rigidbody>();
        readonly HashSet<Rigidbody> found = new HashSet<Rigidbody>();
        readonly List<Rigidbody> gone = new List<Rigidbody>();
        static readonly Collider[] hits = new Collider[64];
        Rigidbody own;

        /// <summary>What is in the bed now (every machine).</summary>
        public IReadOnlyList<Rigidbody> Contents => contents;

        public int Count => contents.Count;

        /// <summary>Something came into the bed (every machine).</summary>
        public event Action<Rigidbody> Entered;

        /// <summary>Something left the bed, or was destroyed in it (every machine).</summary>
        public event Action<Rigidbody> Left;

        public Vector3 Center { get => center; set => center = value; }
        public Vector3 Size { get => size; set => size = value; }

        protected virtual void Awake() => own = GetComponentInParent<Rigidbody>();

        public bool Contains(Rigidbody body) => body != null && contents.Contains(body);

        /// <summary>Is this component on something in the bed?</summary>
        public bool Contains(Component thing)
        {
            if (thing == null) return false;
            var body = thing.GetComponentInParent<Rigidbody>();
            return Contains(body);
        }

        /// <summary>For tools: where the load space is (local space).</summary>
        public void Setup(Vector3 localCenter, Vector3 localSize)
        {
            center = localCenter;
            size = localSize;
        }

        void FixedUpdate()
        {
            Refresh();
            if (hold <= 0f) return;

            Vector3 up = transform.up;
            foreach (Rigidbody body in contents)
            {
                if (body == null || body.isKinematic || body.mass > maxMass) continue;
                if (body.TryGetComponent(out IHoldable holdable) && holdable.IsHeld) continue;

                Vector3 bedVelocity = own != null ? own.GetPointVelocity(body.worldCenterOfMass) : Vector3.zero;
                Vector3 change = Vector3.ProjectOnPlane(bedVelocity - body.linearVelocity, up);
                Vector3 acceleration = Vector3.ClampMagnitude(change * hold, maxAcceleration);
                if (acceleration.sqrMagnitude < 1e-6f) continue;
                body.AddForce(acceleration, ForceMode.Acceleration);
                if (own != null && !own.isKinematic)
                    own.AddForceAtPosition(-acceleration * body.mass, body.worldCenterOfMass, ForceMode.Force);
            }
        }

        // Physics queries, so it works the same on every machine and needs no trigger collider.
        void Refresh()
        {
            found.Clear();
            Vector3 world = transform.TransformPoint(center);
            Vector3 half = Vector3.Scale(size, Abs(transform.lossyScale)) * 0.5f;
            int count = Physics.OverlapBoxNonAlloc(world, half, hits, transform.rotation, layers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = hits[i].attachedRigidbody;
                if (body == null || body == own) continue;
                found.Add(body);
            }

            gone.Clear();
            foreach (Rigidbody body in contents)
                if (body == null || !found.Contains(body)) gone.Add(body);
            foreach (Rigidbody body in gone)
            {
                contents.Remove(body);
                Left?.Invoke(body);
            }
            foreach (Rigidbody body in found)
            {
                if (contents.Contains(body)) continue;
                contents.Add(body);
                Entered?.Invoke(body);
            }
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        void OnDrawGizmosSelected()
        {
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.55f, 0.9f, 0.45f, 0.2f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.55f, 0.9f, 0.45f);
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previous;
        }
    }
}
