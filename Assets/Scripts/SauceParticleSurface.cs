using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2.5D sauce: equal-volume simulation samples + a density-contoured surface.
/// PanSauce alone owns ingredients/volume/heat. These samples are visual parcels,
/// not extra portions, colliders, or independent GameObjects.
/// </summary>
public sealed class SauceParticleSurface : MonoBehaviour
{
    private const int Count = 96, Grid = 48;
    private readonly Vector2[] positions = new Vector2[Count];
    private readonly Vector2[] velocities = new Vector2[Count];
    private readonly float[] heights = new float[(Grid + 1) * (Grid + 1)];
    private readonly Vector3[] normals = new Vector3[(Grid + 1) * (Grid + 1)];
    private readonly List<Vector3> vertices = new List<Vector3>(Grid * Grid * 12);
    private readonly List<Vector3> meshNormals = new List<Vector3>(Grid * Grid * 12);
    private readonly List<int> triangles = new List<int>(Grid * Grid * 12);
    private readonly List<FoodPieceGroup> groups = new List<FoodPieceGroup>();
    private PanSauce source;
    private MovableBowl bowl;
    private Mesh mesh;
    private Material material;
    private float radius, spread, previousSpread, accumulator, meshClock;
    private bool initialized;
    public int ParticleCount => Count;
    public int SurfaceTriangleCount => triangles.Count / 3;
    public float MeanRadius { get { float sum = 0f; foreach (var p in positions) sum += p.magnitude; return sum / Count; } }
    private float Viscosity => Mathf.Clamp01(source.Thickness);

    public void Configure(PanSauce owner, MovableBowl vessel, float visibleRadius)
    {
        source = owner;
        bowl = vessel;
        radius = Mathf.Max(0.03f, bowl.GetInterior().radius * 0.96f);
        spread = Mathf.Clamp(visibleRadius, 0.015f, radius);
        if (!initialized)
        {
            initialized = true;
            previousSpread = spread;
            for (int i = 0; i < Count; i++)
            {
                float angle = i * 2.399963f;
                positions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                    * (spread * 0.88f * Mathf.Sqrt((i + 0.5f) / Count));
            }
            mesh = new Mesh { name = "Connected sauce surface" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.SetFloat("_Smoothness", 0.88f);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        // Evaporation/pouring changes parcel volume; retain the existing flow layout.
        // A little contraction avoids a thin, disconnected remnant after most is poured out.
        if (spread < previousSpread)
        {
            float ratio = Mathf.Lerp(1f, spread / Mathf.Max(previousSpread, 0.001f), 0.35f);
            for (int i = 0; i < Count; i++) positions[i] *= ratio;
        }
        previousSpread = spread;
        material.color = source.Color;
    }

    public void Impulse(Vector3 worldVelocity)
    {
        Vector3 local = transform.InverseTransformDirection(worldVelocity);
        Vector2 impulse = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 0.7f);
        for (int i = 0; i < Count; i++) velocities[i] = Vector2.ClampMagnitude(velocities[i] + impulse, 1.6f);
    }

    /// <summary>A swept disk pushes only parcels touched by the spoon's path.</summary>
    public void Sweep(Vector3 worldFrom, Vector3 worldTo, float reach)
    {
        if (!initialized) return;
        Vector3 f = transform.InverseTransformPoint(worldFrom), t = transform.InverseTransformPoint(worldTo);
        Vector2 a = new Vector2(f.x, f.z), b = new Vector2(t.x, t.z), travel = b - a;
        if (a.magnitude > radius * 1.5f && b.magnitude > radius * 1.5f) return;
        reach = Mathf.Clamp(reach, radius * 0.08f, radius * 0.35f);
        Vector2 direction = travel.sqrMagnitude > 1e-7f ? travel.normalized : Vector2.right;
        for (int i = 0; i < Count; i++)
        {
            float along = Mathf.Clamp01(Vector2.Dot(positions[i] - a, travel) / Mathf.Max(travel.sqrMagnitude, 1e-7f));
            Vector2 nearest = a + travel * along;
            Vector2 offset = positions[i] - nearest;
            float distance = offset.magnitude;
            if (distance >= reach) continue;
            Vector2 outward = distance > 0.0001f ? offset / distance : new Vector2(-direction.y, direction.x);
            float contact = 1f - distance / reach;
            positions[i] += outward * (reach - distance) * 0.8f;
            velocities[i] = Vector2.ClampMagnitude(velocities[i] +
                (direction * Mathf.Min(travel.magnitude * 14f, 0.7f) + outward * 0.22f) * contact, 1.6f);
        }
        meshClock = 1f; // rebuild the cleared trail on the next frame
    }

    void LateUpdate()
    {
        if (!initialized || source == null || bowl == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        accumulator = Mathf.Min(accumulator + dt, 0.06f);
        bowl.GetComponentsInChildren(groups);
        while (accumulator >= 1f / 60f)
        {
            Step(1f / 60f);
            accumulator -= 1f / 60f;
        }
        meshClock += dt;
        if (meshClock >= 1f / 30f || mesh.vertexCount == 0)
        {
            BuildSurface();
            meshClock = 0f;
        }
    }

    private void Step(float dt)
    {
        float viscosity = Viscosity;
        float spacing = Mathf.Max(spread * Mathf.Sqrt(Mathf.PI / Count) * 0.9f, radius * 0.025f);
        Vector3 gravity = bowl.IsHeld ? transform.InverseTransformDirection(-Carryable.CounterUp) : Vector3.zero;
        Vector2 downhill = new Vector2(gravity.x, gravity.z) * 2f;
        if (source.PourLip.HasValue)
        {
            Vector3 lip = transform.InverseTransformPoint(source.PourLip.Value);
            downhill += new Vector2(lip.x, lip.z).normalized * 1.5f;
        }
        // Pair forces: short-range separation, gentle attraction, velocity sharing.
        for (int i = 0; i < Count; i++)
            for (int j = i + 1; j < Count; j++)
            {
                Vector2 d = positions[j] - positions[i];
                float distance = d.magnitude;
                if (distance < 0.00001f || distance > spacing * 2.4f) continue;
                Vector2 n = d / distance;
                if (distance < spacing)
                {
                    Vector2 correction = n * (spacing - distance) * 0.45f;
                    positions[i] -= correction;
                    positions[j] += correction;
                }
                else
                {
                    Vector2 cohesion = n * ((distance - spacing) * dt * Mathf.Lerp(0.6f, 0.2f, viscosity));
                    velocities[i] += cohesion;
                    velocities[j] -= cohesion;
                }
                Vector2 share = (velocities[j] - velocities[i]) *
                    (dt * Mathf.Lerp(0.8f, 4f, viscosity) * (1f - distance / (spacing * 2.4f)));
                velocities[i] += share;
                velocities[j] -= share;
            }
        for (int i = 0; i < Count; i++)
        {
            velocities[i] += downhill * dt;
            velocities[i] *= Mathf.Exp(-dt * Mathf.Lerp(1.8f, 7f, viscosity));
            positions[i] += velocities[i] * dt;
        }
        InteractWithFood(dt);
        float wall = Mathf.Max(radius - spacing * 0.3f, 0.01f);
        for (int i = 0; i < Count; i++)
            if (positions[i].magnitude > wall)
            {
                Vector2 n = positions[i].normalized;
                positions[i] = n * wall;
                float outward = Vector2.Dot(velocities[i], n);
                if (outward > 0f) velocities[i] -= n * outward * 1.15f;
            }
    }

    private void InteractWithFood(float dt)
    {
        var interior = bowl.GetInterior();
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, interior.up).normalized;
        Quaternion foodFrame = Quaternion.LookRotation(Vector3.Cross(right, interior.up).normalized, interior.up);
        foreach (var group in groups)
        {
            if (group.Container != bowl) continue;
            foreach (var piece in group.Pieces)
            {
                if (piece == null) continue;
                Vector3 at = transform.InverseTransformPoint(piece.transform.position);
                if (at.y > piece.Radius * 2f + 0.08f) continue;
                Vector2 center = new Vector2(at.x, at.z);
                Vector2 flow = Vector2.zero;
                float weight = 0f;
                bool obstacle = piece.Radius > radius * 0.085f;
                float reach = Mathf.Max(piece.Radius * 0.8f, radius * 0.07f);
                for (int i = 0; i < Count; i++)
                {
                    Vector2 d = positions[i] - center;
                    float distance = d.magnitude;
                    if (distance > reach * 2f) continue;
                    float w = 1f - distance / (reach * 2f);
                    flow += velocities[i] * w;
                    weight += w;
                    if (obstacle && distance < reach && distance > 0.0001f)
                    {
                        Vector2 n = d / distance;
                        positions[i] += n * (reach - distance) * 0.35f;
                        float into = Vector2.Dot(velocities[i], n);
                        if (into < 0f) velocities[i] -= n * into * 0.7f;
                    }
                }
                // Tiny mince follows the local liquid; large tofu remains an obstacle.
                if (!obstacle && weight > 0f)
                {
                    Vector2 v = flow / weight;
                    Vector3 world = transform.TransformDirection(new Vector3(v.x, 0f, v.y));
                    Vector3 local = Quaternion.Inverse(foodFrame) * world;
                    piece.simVel += new Vector3(local.x - piece.simVel.x, 0f, local.z - piece.simVel.z) * Mathf.Clamp01(dt * 3f);
                }
            }
        }
    }

    // Deposit compact kernels into a height field, then contour the field. Empty
    // cells have no triangles, so a spoon trail exposes the actual pan underneath.
    private void BuildSurface()
    {
        System.Array.Clear(heights, 0, heights.Length);
        float cell = radius * 2f / Grid;
        float support = Mathf.Max(spread * 0.26f, cell * 2.2f);
        float volumePerParcel = Mathf.PI * spread * spread * 0.025f / Count;
        float scale = volumePerParcel * 3f / (Mathf.PI * support * support);
        for (int i = 0; i < Count; i++)
        {
            Vector2 p = positions[i];
            int x0 = Mathf.Clamp(Mathf.FloorToInt((p.x - support + radius) / cell), 0, Grid);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((p.x + support + radius) / cell), 0, Grid);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((p.y - support + radius) / cell), 0, Grid);
            int z1 = Mathf.Clamp(Mathf.CeilToInt((p.y + support + radius) / cell), 0, Grid);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 at = new Vector2(x * cell - radius, z * cell - radius);
                    float q = 1f - (at - p).sqrMagnitude / (support * support);
                    if (q > 0f && at.sqrMagnitude < radius * radius)
                        heights[z * (Grid + 1) + x] += q * q * scale;
                }
        }
        for (int z = 0; z <= Grid; z++)
            for (int x = 0; x <= Grid; x++)
            {
                int index = z * (Grid + 1) + x;
                float dx = heights[z * (Grid + 1) + Mathf.Min(x + 1, Grid)] - heights[z * (Grid + 1) + Mathf.Max(x - 1, 0)];
                float dz = heights[Mathf.Min(z + 1, Grid) * (Grid + 1) + x] - heights[Mathf.Max(z - 1, 0) * (Grid + 1) + x];
                normals[index] = new Vector3(-dx / (2f * cell), 1f, -dz / (2f * cell)).normalized;
            }
        vertices.Clear(); meshNormals.Clear(); triangles.Clear();
        for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int a = z * (Grid + 1) + x, b = a + 1, c = a + Grid + 1, d = c + 1;
                Contour(Point(a, cell), Point(c, cell), Point(b, cell));
                Contour(Point(b, cell), Point(c, cell), Point(d, cell));
            }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(meshNormals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private const float Edge = 0.003f;
    private struct Sample { public Vector3 p, n; public float h; }
    private Sample Point(int index, float cell)
    {
        float h = heights[index];
        return new Sample { p = new Vector3(index % (Grid + 1) * cell - radius,
            Mathf.Min(h, 0.09f), index / (Grid + 1) * cell - radius), n = normals[index], h = h };
    }
    private Sample Cut(Sample a, Sample b)
    {
        float t = Mathf.Clamp01((Edge - a.h) / (b.h - a.h));
        return new Sample { p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, h = Edge };
    }
    private void Contour(Sample a, Sample b, Sample c)
    {
        int mask = (a.h >= Edge ? 1 : 0) | (b.h >= Edge ? 2 : 0) | (c.h >= Edge ? 4 : 0);
        switch (mask)
        {
            case 0: return;
            case 7: Triangle(a, b, c); return;
            case 1: Triangle(a, Cut(a,b), Cut(a,c)); return;
            case 2: Triangle(b, Cut(b,c), Cut(b,a)); return;
            case 4: Triangle(c, Cut(c,a), Cut(c,b)); return;
            case 3: Quad(a,b,Cut(b,c),Cut(a,c)); return;
            case 6: Quad(b,c,Cut(c,a),Cut(b,a)); return;
            case 5: Quad(c,a,Cut(a,b),Cut(c,b)); return;
        }
    }
    private void Quad(Sample a, Sample b, Sample c, Sample d) { Triangle(a,b,c); Triangle(a,c,d); }
    private void Triangle(Sample a, Sample b, Sample c)
    {
        int start = vertices.Count;
        vertices.Add(a.p); vertices.Add(b.p); vertices.Add(c.p);
        meshNormals.Add(a.n); meshNormals.Add(b.n); meshNormals.Add(c.n);
        triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
    }
    public float HeightAtWorld(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world);
        int x = Mathf.Clamp(Mathf.RoundToInt((p.x + radius) / (2f * radius) * Grid), 0, Grid);
        int z = Mathf.Clamp(Mathf.RoundToInt((p.z + radius) / (2f * radius) * Grid), 0, Grid);
        return heights[z * (Grid + 1) + x];
    }
    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
