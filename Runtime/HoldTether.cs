using UnityEngine;

namespace Heft
{
    /// <summary>
    /// Draws a holder's grip as a rope-like tether from their hand to the point they hold, on every machine: it bends
    /// toward where they are pulling (the hold point), sags when slack and straightens under strain, and its colour
    /// goes from Slack to Taut as the grip nears slipping (Grabber.Strain, synced from the server).
    ///
    /// Put it next to the Grabber. The tether is a tube mesh on its own object (not a child of the player), made from
    /// Tether Prefab when set (so a game can add its own components, materials or render settings) or else a plain
    /// MeshFilter + MeshRenderer with Material. Its colour is set on the renderer's own material instance
    /// (_BaseColor and _Color).
    /// </summary>
    [RequireComponent(typeof(Grabber))]
    [DisallowMultipleComponent]
    public class HoldTether : MonoBehaviour
    {
        [Tooltip("Optional: what the tether is made from (MeshFilter + MeshRenderer, and anything else the game needs). Empty = a plain one with Material.")]
        [SerializeField] GameObject tetherPrefab;
        [Tooltip("Used when there is no Tether Prefab.")]
        [SerializeField] Material material;

        [Header("Look")]
        [SerializeField] Color slack = new Color(0.16f, 0.13f, 0.12f, 1f);
        [SerializeField] Color taut = new Color(0.75f, 0.1f, 0.08f, 1f);
        [Tooltip("Strain is shown this much more strongly (above 1 the colour turns sooner).")]
        [SerializeField, Min(0.1f)] float strainCurve = 1.5f;
        [SerializeField, Min(0.002f)] float width = 0.02f;
        [SerializeField, Range(3, 12)] int sides = 6;
        [SerializeField, Range(2, 48)] int segments = 16;
        [Tooltip("How far it bends toward where it is being pulled (0..1).")]
        [SerializeField, Range(0f, 1f)] float bend = 0.35f;
        [Tooltip("How much it sags when slack, as a share of its length.")]
        [SerializeField, Range(0f, 1f)] float sag = 0.12f;

        [Header("Hand")]
        [Tooltip("Where the tether starts, in the Grabber's Aim space (right, up, forward), so it comes from the hand rather than the eye.")]
        [SerializeField] Vector3 handOffset = new Vector3(0.22f, -0.28f, 0.25f);

        Grabber grabber;
        GameObject tether;
        Mesh mesh;
        Renderer tetherRenderer;
        Material tetherMaterial;
        Vector3[] vertices;
        Color shown = new Color(-1f, 0f, 0f, 0f);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The tether's object, once made (null before the first grab).</summary>
        public GameObject Tether => tether;

        protected virtual void Awake() => grabber = GetComponent<Grabber>();

        protected virtual void OnDestroy()
        {
            if (tether != null) Destroy(tether);
            if (mesh != null) Destroy(mesh);
            if (tetherMaterial != null) Destroy(tetherMaterial);
        }

        protected virtual void OnDisable()
        {
            if (tether != null) tether.SetActive(false);
        }

        void LateUpdate()
        {
            bool show = grabber != null && grabber.IsSpawned && grabber.IsHolding;
            if (!show)
            {
                if (tether != null && tether.activeSelf) tether.SetActive(false);
                return;
            }

            if (tether == null && !Make()) return;
            if (!tether.activeSelf) tether.SetActive(true);

            Transform aim = grabber.Aim;
            Vector3 from = aim.TransformPoint(handOffset);
            Vector3 to = grabber.GripPoint;
            float strain = Mathf.Clamp01(Mathf.Pow(grabber.Strain, 1f / strainCurve));
            Vector3 control = CarryMath.TetherControl(from, to, grabber.HoldPoint, bend, sag, grabber.Strain);

            // The tether's object sits at the hand, so a game's moving-texture component travels with it.
            tether.transform.SetPositionAndRotation(from, Quaternion.identity);
            Build(from, control, to);
            SetColor(Color.Lerp(slack, taut, strain));
        }

        bool Make()
        {
            tether = tetherPrefab != null ? Instantiate(tetherPrefab) : new GameObject("Hold Tether");
            tether.name = $"{name} Tether";
            if (!tether.TryGetComponent(out MeshFilter filter)) filter = tether.AddComponent<MeshFilter>();
            if (!tether.TryGetComponent(out tetherRenderer))
            {
                var meshRenderer = tether.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                tetherRenderer = meshRenderer;
            }
            tetherRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (tetherRenderer.sharedMaterial != null) tetherMaterial = tetherRenderer.material; // its own copy, for its colour

            mesh = new Mesh { name = "Hold Tether" };
            mesh.MarkDynamic();
            int rings = segments + 1;
            vertices = new Vector3[rings * sides];
            var triangles = new int[segments * sides * 6];
            int t = 0;
            for (int s = 0; s < segments; s++)
            for (int k = 0; k < sides; k++)
            {
                int a = s * sides + k, b = s * sides + (k + 1) % sides;
                int c = a + sides, d = b + sides;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            filter.sharedMesh = mesh;
            tether.SetActive(false);
            return true;
        }

        void Build(Vector3 from, Vector3 control, Vector3 to)
        {
            Vector3 origin = from;
            Vector3 previousNormal = Vector3.zero;
            float radius = width * 0.5f;
            for (int s = 0; s <= segments; s++)
            {
                float t = s / (float)segments;
                Vector3 p = CarryMath.Curve(from, control, to, t);
                Vector3 ahead = CarryMath.Curve(from, control, to, Mathf.Min(1f, t + 0.01f));
                Vector3 behind = CarryMath.Curve(from, control, to, Mathf.Max(0f, t - 0.01f));
                Vector3 tangent = ahead - behind;
                if (tangent.sqrMagnitude < 1e-10f) tangent = Vector3.forward;
                tangent.Normalize();

                // A normal that turns as little as it can along the curve, so the tube doesn't twist.
                Vector3 normal = s == 0 ? Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.95f ? Vector3.up : Vector3.right)
                    : Vector3.ProjectOnPlane(previousNormal, tangent);
                if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(tangent, Vector3.right);
                normal.Normalize();
                previousNormal = normal;
                Vector3 binormal = Vector3.Cross(tangent, normal);

                for (int k = 0; k < sides; k++)
                {
                    float angle = k * Mathf.PI * 2f / sides;
                    Vector3 offset = (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius;
                    vertices[s * sides + k] = p + offset - origin;
                }
            }
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        void SetColor(Color color)
        {
            if (tetherMaterial == null || color == shown) return;
            shown = color;
            if (tetherMaterial.HasProperty(BaseColor)) tetherMaterial.SetColor(BaseColor, color);
            if (tetherMaterial.HasProperty(ColorId)) tetherMaterial.SetColor(ColorId, color);
        }
    }
}
