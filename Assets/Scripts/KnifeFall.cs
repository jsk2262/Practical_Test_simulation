using UnityEngine;

/// <summary>
/// The knife left lying on the cutting board when the board is lifted
/// (BoardSinkWash): it isn't carried along - freed from the board where it
/// lies (world position and turn kept), it first slides across the counter
/// toward its front edge (pushed a little against the way the board is
/// moved), and once it's past that edge, a Rigidbody takes over: it
/// falls under gravity and comes to rest below the counter's edge by its own
/// collisions - on the shelves standing there, or on an invisible floor
/// surface at the cabinet's foot (the floor itself has no collider). Then
/// it's an ordinary knife again, lying where it fell - dirty (to be washed
/// before it's used again) and picked up as usual.
/// Only one thing moves it at a time: the slide drives the transform while
/// the body is absent; afterwards only the Rigidbody does. It can't be picked
/// up while it's falling (HandCarry asks IsFalling).
/// A safety record for each fall (only one per fall, however many bumps),
/// up to a cap; nothing is shown while cooking.
/// </summary>
public class KnifeFall : MonoBehaviour
{
    /// <summary>The record's reason, as shown in the final results.</summary>
    public const string Reason = "칼이 놓인 도마를 들어 올려 칼이 떨어졌습니다.";

    public static bool IsFalling(Component c) => c != null && c.GetComponent<KnifeFall>() != null;

    private static int recorded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() { recorded = 0; catcher = null; lipBox = null; }

    private ToolItem knife;
    private Transform board;
    private float floorY, floorZ;
    private Collider[] ownColliders;
    private BoxCollider body;
    private Rigidbody rb;
    private bool done;
    private float stillFor, physicsFor;

    /// <summary>Starts it (the board has just been taken up). maxRecords: how
    /// many falls are recorded at most (each one its own record).</summary>
    public static void Begin(ToolItem knife, Transform board, float floorY, float floorZ, int maxRecords)
    {
        if (knife == null || IsFalling(knife)) return;
        // (only a knife lying there: not one in the hand)
        if (HandCarry.Instance != null && (object)HandCarry.Instance.Current == knife) return;

        knife.CurrentContainer?.Release(knife);
        knife.CurrentContainer = null;
        knife.transform.SetParent(null, true); // (where it lies, as it lies)

        KnifeFall f = knife.gameObject.AddComponent<KnifeFall>();
        f.knife = knife;
        f.board = board;
        f.floorY = floorY;
        f.floorZ = floorZ;

        recorded++;
        if (recorded <= Mathf.Max(1, maxRecords))
            ExamDeductions.Add(ExamDeductions.Safety, recorded == 1 ? Reason : $"{Reason} ({recorded}회째)");
        Debug.Log($"[도마] 도마 위에 있던 칼이 미끄러져 떨어짐 ({recorded}번째)");
    }

    // ---- 1) sliding off the board ----

    private Vector3 slideVel;
    private Vector3 lastBoardPos;
    private bool sliding = true;
    private float spinDeg;
    private Renderer[] ownRenderers;
    private Vector3 toFront;        // 조리대 윗면에 누운 '앞쪽'(플레이어가 보는 쪽) 방향
    private float frontEdge;        // 그 축에서 조리대 앞턱의 가장 앞 지점

    void Start()
    {
        ownColliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in ownColliders) c.enabled = false; // (not clicked or bumped while it slides)
        ownRenderers = GetComponentsInChildren<Renderer>(); // 매 프레임 다시 찾지 않게 한 번만 모아 둔다
        lastBoardPos = board != null ? board.position : transform.position;
        spinDeg = Random.Range(-40f, 40f);

        // 조리대는 수평이라 '내리막'이 없다. 칼은 조리대가 열려 있는 앞쪽으로 밀려 떨어진다.
        Camera cam = Camera.main;
        Vector3 up = Carryable.CounterUp; // 조리대 윗면의 법선
        toFront = cam != null ? Vector3.ProjectOnPlane(-cam.transform.forward, up).normalized : Vector3.forward; // 조리대 면에 투영한 카메라 쪽
        Bounds lip = CounterLip(); // 씬에 서 있는 조리대 앞턱
        frontEdge = Vector3.Dot(lip.center, toFront)
                  + Mathf.Abs(lip.extents.x * toFront.x) + Mathf.Abs(lip.extents.y * toFront.y) + Mathf.Abs(lip.extents.z * toFront.z); // 앞턱의 가장 앞까지
    }

    void Update()
    {
        if (!sliding || done) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Across the counter toward its front edge, the board pulled away under
        // it leaving it behind a little; gently - it slips, it isn't thrown.
        Vector3 up = Carryable.CounterUp; // 조리대 윗면의 법선
        Vector3 boardVel = board != null ? (board.position - lastBoardPos) / dt : Vector3.zero; // 도마가 빠져나가는 속도
        if (board != null) lastBoardPos = board.position;
        Vector3 drag = Vector3.ProjectOnPlane(-boardVel, up); // 도마에 끌려 반대쪽으로 밀리는 몫
        slideVel += (toFront * 6f + Vector3.ClampMagnitude(drag, 3f) * 0.6f) * dt; // 앞쪽으로 천천히 가속
        slideVel = Vector3.ClampMagnitude(slideVel, 2.2f);
        transform.position += slideVel * dt;
        transform.rotation = Quaternion.AngleAxis(spinDeg * dt, up) * transform.rotation;

        // Past the counter's front edge: it falls from here.
        if (Vector3.Dot(Bounds().center, toFront) > frontEdge) StartFalling(); // 무게중심이 앞턱을 넘으면 낙하 시작
    }

    // ---- 2) falling: the Rigidbody only ----

    private void StartFalling()
    {
        sliding = false;
        // 앞턱을 넘은 자리에서 수납장 문 바로 앞으로만 살짝 밀어 내고, 그 평면 안에서 떨어진다.
        transform.position = new Vector3(transform.position.x, transform.position.y, floorZ);

        // A simple box for the body (its own mesh collider isn't convex).
        Bounds lb = LocalBounds();
        body = gameObject.AddComponent<BoxCollider>();
        body.center = lb.center;
        // (at least a few hundredths of a world unit thick - its own space is scaled up a lot)
        Vector3 s = transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 1e-4f);
        body.size = Vector3.Max(lb.size, Vector3.one * (0.04f / scale));
        body.sharedMaterial = new PhysicsMaterial("KnifeFall") { bounciness = 0.25f, dynamicFriction = 0.6f, staticFriction = 0.7f };

        // (it's off the board now: the board, carried on under the mouse, doesn't catch it again)
        if (board != null)
            foreach (Collider bc in board.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(body, bc);

        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 0.4f;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.8f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 12f;
        // (it stays in the picture: down and across the screen, turning in it)
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        Vector3 v = slideVel; v.z = 0f;
        rb.linearVelocity = Vector3.ClampMagnitude(v, 1.5f);
        rb.angularVelocity = new Vector3(0f, 0f, -Mathf.Sign(v.x == 0f ? 1f : v.x) * Random.Range(2f, 4f));
        // (the scene is about 10 units to the metre: gravity to match, not slow-motion)
        ConstantForce extra = gameObject.AddComponent<ConstantForce>();
        extra.force = Physics.gravity * rb.mass * 2f;

        Catcher(floorY, floorZ);
    }

    void FixedUpdate()
    {
        if (rb == null || done) return;
        physicsFor += Time.fixedDeltaTime;
        stillFor = rb.linearVelocity.sqrMagnitude < 0.01f && rb.angularVelocity.sqrMagnitude < 0.05f ? stillFor + Time.fixedDeltaTime : 0f;
        if (stillFor > 0.35f || physicsFor > 5f) Finish();
    }

    // ---- 3) at rest: an ordinary knife again ----

    private void Finish()
    {
        if (done) return;
        done = true;
        knife?.GetComponent<HygieneTracker>()?.MarkNeedsWash(); // 조리대 아래로 떨어진 칼은 어디에 닿았든 다시 씻어야 한다
        CleanUp();
        Debug.Log("[도마] 칼이 조리대 아래로 떨어져 멈춤 - 다시 집을 수 있음");
        Destroy(this);
    }

    private void CleanUp()
    {
        if (GetComponent<ConstantForce>() is ConstantForce f) Destroy(f);
        if (rb != null) Destroy(rb);
        if (body != null) Destroy(body);
        rb = null;
        body = null;
        if (ownColliders != null) foreach (Collider c in ownColliders) if (c != null) c.enabled = true;
    }

    /// <summary>Stopped part-way (switched off, the scene closed): never left
    /// hanging in the air or unclickable - it's put down on the floor right
    /// below where it was.</summary>
    void OnDisable()
    {
        if (done) return;
        done = true;
        knife?.GetComponent<HygieneTracker>()?.MarkNeedsWash();
        CleanUp();
        Vector3 p = transform.position;
        transform.position = new Vector3(p.x, Mathf.Min(p.y, floorY), floorZ);
        Destroy(this);
    }

    // ---- helpers ----

    private Bounds Bounds()
    {
        Renderer[] rs = ownRenderers; // Start 에서 모아 둔 렌더러들
        if (rs == null || rs.Length == 0) return new Bounds(transform.position, Vector3.one * 0.1f);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Its renderers' box in its own space (for the body's collider).</summary>
    private Bounds LocalBounds()
    {
        // Its own meshes' boxes, corner by corner into its space (not its world
        // box - turned and scaled as it lies, that came out several times too big).
        bool any = false;
        Bounds lb = default;
        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { lb = new Bounds(local, Vector3.zero); any = true; }
                else lb.Encapsulate(local);
            }
        }
        return any ? lb : new Bounds(Vector3.zero, Vector3.one * 0.1f);
    }

    private static Bounds? lipBox;
    /// <summary>The counter's front lip, as it stands in the scene.</summary>
    private static Bounds CounterLip()
    {
        if (!lipBox.HasValue)
        {
            GameObject lip = GameObject.Find("Counter_FrontLip");
            Renderer r = lip != null ? lip.GetComponent<Renderer>() : null;
            lipBox = r != null ? r.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f); // 못 찾으면 원점의 작은 상자
        }
        return lipBox.Value;
    }

    private static GameObject catcher;

    /// <summary>The floor's surface for things that fall (the floor model has
    /// no collider): unseen, and not in the way of clicks (Ignore Raycast).</summary>
    private static void Catcher(float floorY, float floorZ)
    {
        if (catcher != null) return;
        catcher = new GameObject("FloorCatcher (떨어진 물건)");
        catcher.layer = 2; // Ignore Raycast
        BoxCollider box = catcher.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, floorY - 0.5f, floorZ);
        box.size = new Vector3(60f, 1f, 2f);
        box.sharedMaterial = new PhysicsMaterial("Floor") { bounciness = 0.2f, dynamicFriction = 0.7f, staticFriction = 0.8f };
    }
}
