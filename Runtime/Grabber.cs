using System;
using System.Collections.Generic;
using CharacterStats;
using GameplayTags;
using Unity.Netcode;
using UnityEngine;

namespace Heft
{
    /// <summary>
    /// Lets a networked player grab and carry Carryables. Put it on the player root (with a NetworkObject, and a
    /// StatSheet for strength and reach). Nothing here reads input: the owner's input code calls TryGrab, Release,
    /// Throw and AdjustHoldDistance.
    ///
    /// The owner aims (a ray from Aim, as long as their reach) and sends where they want their grip each frame; the
    /// server checks a grab, holds the grip and moves the object (see Carryable). What is held is synced, so every
    /// machine knows (Holding, HoldingChanged, the Carrying tag) and can put the player's weapons away.
    /// </summary>
    [DisallowMultipleComponent]
    public class Grabber : NetworkBehaviour
    {
        [Tooltip("Where the player looks from: the camera or head. Its forward is the aim.")]
        [SerializeField] Transform aim;
        [Tooltip("What the grab ray can hit (walls included, so you can't grab through them).")]
        [SerializeField] LayerMask grabMask = Physics.DefaultRaycastLayers;

        [Header("Strength")]
        [Tooltip("Optional. The stat for strength. Empty = Default Strength.")]
        [SerializeField] StatDefinition strengthStat;
        [SerializeField, Min(0f)] float defaultStrength = 10f;
        [Tooltip("Newtons of grip force per point of strength. With gravity at 9.81, strength 10 and 15 N per point holds up about 15 kg alone.")]
        [SerializeField, Min(0f)] float forcePerStrength = 15f;

        [Header("Reach")]
        [Tooltip("Optional. The stat for reach, in metres. Empty = Default Reach.")]
        [SerializeField] StatDefinition reachStat;
        [SerializeField, Min(0.1f)] float defaultReach = 2.5f;
        [Tooltip("The server allows grabs this much further than reach, for lag.")]
        [SerializeField, Min(0f)] float reachTolerance = 1f;
        [Tooltip("Closest the hold point can be pulled in.")]
        [SerializeField, Min(0.1f)] float minHoldDistance = 0.9f;

        [Header("Throwing")]
        [Tooltip("Throw impulse (N s) per point of strength.")]
        [SerializeField, Min(0f)] float throwImpulsePerStrength = 1.5f;
        [SerializeField, Min(0f)] float maxThrowSpeed = 12f;

        [Header("Tags")]
        [Tooltip("Optional. Held on every machine while carrying something (condition modifiers on it).")]
        [SerializeField] GameplayTag carryingTag;
        [Tooltip("Holding any of these (or their children) makes the player let go and stops them grabbing: knocked out, dead...")]
        [SerializeField] List<GameplayTag> blockedBy = new List<GameplayTag>();

        readonly NetworkVariable<HoldAim> aimState = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        const ulong None = ulong.MaxValue;
        readonly NetworkVariable<ulong> held = new(None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        StatSheet sheet;
        TagHolder tags;
        Collider[] ownColliders;
        Carryable holding;
        bool carryingTagAdded;
        float holdDistance;
        Quaternion holdTurn = Quaternion.identity;
        Vector3 lastPoint;
        Vector3 smoothedVelocity;
        bool hasLastPoint;
        float aimReceivedAt;
        HoldAim grabAim;
        bool useGrabAim;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>What this player is holding (every machine), or null.</summary>
        public Carryable Holding => holding;
        public bool IsHolding => holding != null;

        /// <summary>Owner: the Carryable TryGrab would take now (for a crosshair), or null.</summary>
        public Carryable Hovered { get; private set; }

        /// <summary>Holding changed (every machine). Null = let go.</summary>
        public event Action<Carryable> HoldingChanged;

        public Transform Aim => aim != null ? aim : transform;
        public float Strength => sheet != null && strengthStat != null ? sheet.Get(strengthStat) : defaultStrength;
        public float Reach => sheet != null && reachStat != null ? sheet.Get(reachStat) : defaultReach;

        /// <summary>The most force this player's grip can apply, in newtons.</summary>
        public float MaxForce => Strength * forcePerStrength;

        /// <summary>Owner: how far ahead the hold point is.</summary>
        public float HoldDistance => holdDistance;

        /// <summary>Owner: how they have turned what they hold (see <see cref="Rotate"/>), relative to their facing.</summary>
        public Quaternion HoldTurn => holdTurn;

        /// <summary>
        /// How hard what they hold is for them: the weight of their share against their max force. Under 1 they can
        /// lift it; 1 or more and it drags. 0 when not holding. For slowing the player down, or UI.
        /// </summary>
        public float Load => holding == null ? 0f
            : CarryMath.Load(holding.Mass, 1f / Mathf.Max(1, holding.HolderCount),
                holding.Body.useGravity ? Physics.gravity.magnitude : 0f, MaxForce);

        /// <summary>False while the player has a Blocked By tag.</summary>
        public bool CanHold
        {
            get
            {
                if (tags == null) return true;
                foreach (GameplayTag tag in blockedBy)
                    if (tags.Has(tag)) return false;
                return true;
            }
        }

        /// <summary>Server: where the owner wants their grip, nudged forward by how old the last update is.</summary>
        internal HoldAim ServerAim
        {
            get
            {
                HoldAim value = useGrabAim ? grabAim : aimState.Value;
                if (!IsOwner) value.Point += value.Velocity * Mathf.Min(0.1f, Time.time - aimReceivedAt);
                return value;
            }
        }

        void Awake()
        {
            sheet = GetComponent<StatSheet>();
            tags = GetComponent<TagHolder>();
            ownColliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            held.OnValueChanged += OnHeldChanged;
            aimState.OnValueChanged += OnAimChanged;
            Resolve();
        }

        public override void OnNetworkDespawn()
        {
            held.OnValueChanged -= OnHeldChanged;
            aimState.OnValueChanged -= OnAimChanged;
            if (IsServer && holding != null && holding.IsSpawned) holding.RemoveGrip(this);
            SetHolding(null);
        }

        void OnHeldChanged(ulong previous, ulong current) => Resolve();
        void OnAimChanged(HoldAim previous, HoldAim current)
        {
            aimReceivedAt = Time.time;
            useGrabAim = false;
        }

        void Resolve()
        {
            Carryable target = null;
            if (held.Value != None && NetworkManager != null
                && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(held.Value, out NetworkObject networkObject))
                networkObject.TryGetComponent(out target);
            SetHolding(target);
        }

        void SetHolding(Carryable target)
        {
            if (target == holding) return;

            if (holding != null) IgnoreCollisions(holding, false);
            holding = target;
            if (holding != null) IgnoreCollisions(holding, true);

            bool wantTag = holding != null && carryingTag != null && tags != null;
            if (wantTag != carryingTagAdded)
            {
                if (wantTag) tags.Add(carryingTag);
                else tags.Remove(carryingTag);
                carryingTagAdded = wantTag;
            }

            HoldingChanged?.Invoke(holding);
        }

        // The holder doesn't bump into (or stand on and lift) what they hold.
        void IgnoreCollisions(Carryable target, bool ignore)
        {
            if (target == null) return;
            foreach (Collider own in ownColliders)
            {
                if (own == null) continue;
                foreach (Collider other in target.Colliders)
                    if (other != null) Physics.IgnoreCollision(own, other, ignore);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;

            // The held object despawned (delivered, destroyed): let go here even before the server says so.
            if (holding != null && (!holding || !holding.IsSpawned))
            {
                SetHolding(null);
                if (IsServer) held.Value = None;
            }
            // Told about a grab before the object was known here: try again.
            else if (holding == null && held.Value != None) Resolve();

            if (!IsOwner) return;

            Hovered = holding == null && CanHold ? FindTarget(out _) : null;
            if (holding != null && !CanHold) Release();
            SendAim(false);
        }

        void SendAim(bool force)
        {
            Transform from = Aim;
            Vector3 point = from.position + from.forward * holdDistance;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 velocity = hasLastPoint ? (point - lastPoint) / dt : Vector3.zero;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1f - Mathf.Exp(-20f * dt));
            lastPoint = point;
            hasLastPoint = true;

            if (holding == null && !force) return;

            var next = new HoldAim { Point = point, Velocity = smoothedVelocity, Forward = from.forward, Rotation = holdTurn };
            HoldAim current = aimState.Value;
            if (force || (next.Point - current.Point).sqrMagnitude > 1e-6f || (next.Velocity - current.Velocity).sqrMagnitude > 1e-4f
                || Vector3.Angle(next.Forward, current.Forward) > 0.5f
                || Quaternion.Angle(next.Rotation, CarryMath.Normalized(current.Rotation)) > 0.25f)
                aimState.Value = next;
        }

        Carryable FindTarget(out RaycastHit found)
        {
            found = default;
            Transform from = Aim;
            int count = Physics.RaycastNonAlloc(from.position, from.forward, hits, Reach, grabMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= best) continue;
                best = hit.distance;
                found = hit;
            }
            if (best == float.MaxValue) return null;

            Carryable target = found.collider.GetComponentInParent<Carryable>();
            return target != null && target.IsSpawned && target.HolderCount < target.MaxHolders ? target : null;
        }

        // ---- Owner actions ----

        /// <summary>Owner: grabs the Carryable under the aim within reach. True if a grab was asked for (the server decides).</summary>
        public bool TryGrab()
        {
            if (!IsSpawned || !IsOwner || holding != null || !CanHold) return false;
            Carryable target = FindTarget(out RaycastHit hit);
            if (target == null) return false;

            holdDistance = Mathf.Clamp(hit.distance, minHoldDistance, Reach);
            holdTurn = Quaternion.identity; // each grab starts as it was picked up
            SendAim(true);
            GrabRpc(target.NetworkObject, target.transform.InverseTransformPoint(hit.point), aimState.Value);
            return true;
        }

        /// <summary>Owner: lets go.</summary>
        public void Release()
        {
            if (IsSpawned && IsOwner && holding != null) ReleaseRpc();
        }

        /// <summary>Owner: throws what they hold along the aim, harder the stronger they are.</summary>
        public void Throw()
        {
            if (IsSpawned && IsOwner && holding != null) ThrowRpc(Aim.forward);
        }

        /// <summary>Owner: moves the hold point nearer (negative) or further (positive), within reach.</summary>
        public void AdjustHoldDistance(float delta)
        {
            if (IsOwner) holdDistance = Mathf.Clamp(holdDistance + delta, minHoldDistance, Reach);
        }

        /// <summary>
        /// Owner: turns what they hold, <paramref name="yaw"/> degrees about their up axis and <paramref name="pitch"/>
        /// degrees about their right axis (positive tips the far side up). The turn is kept relative to where they
        /// look for the rest of the grab, and starts again at none on the next grab. With several holders the first
        /// one's turn steers it. How far it actually turns depends on the Carryable's turning and the holder's strength.
        /// </summary>
        public void Rotate(float yaw, float pitch)
        {
            if (!IsOwner || holding == null) return;
            holdTurn = CarryMath.TurnHold(holdTurn, yaw, pitch);
        }

        // ---- Server ----

        [Rpc(SendTo.Server)]
        void GrabRpc(NetworkObjectReference target, Vector3 localPoint, HoldAim aimAtGrab)
        {
            if (holding != null || !CanHold || !target.TryGet(out NetworkObject networkObject)
                || !networkObject.TryGetComponent(out Carryable carryable))
                return;

            Vector3 point = carryable.transform.TransformPoint(localPoint);
            if (Vector3.Distance(Aim.position, point) > Reach + reachTolerance) return;

            // The aim update may arrive after this call: use the aim sent with it until a newer one comes.
            grabAim = aimAtGrab;
            useGrabAim = !IsOwner;
            aimReceivedAt = Time.time;
            if (carryable.AddGrip(this, localPoint, aimAtGrab.Forward))
            {
                held.Value = carryable.NetworkObjectId;
                SetHolding(carryable); // the host sees it at once
            }
        }

        [Rpc(SendTo.Server)]
        void ReleaseRpc()
        {
            if (holding != null) holding.RemoveGrip(this);
        }

        [Rpc(SendTo.Server)]
        void ThrowRpc(Vector3 direction)
        {
            Carryable target = holding;
            if (target == null) return;
            target.RemoveGrip(this);
            target.Throw(direction, Strength * throwImpulsePerStrength, maxThrowSpeed);
        }

        /// <summary>Server: called by the Carryable when this player's grip ends, for whatever reason.</summary>
        internal void ServerLetGo(Carryable target)
        {
            if (holding != null && holding != target) return;
            if (IsSpawned) held.Value = None;
            SetHolding(null);
        }
    }
}
