using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A mesh being cut up (a block of meat): its vertices in one working frame
/// (the cutting rig: x across the board, y up off it, z back), the model's own
/// surfaces kept (their UVs, so the texture stays on them) in its own
/// submeshes, and one more submesh for the faces the cuts make.
/// </summary>
public sealed class SliceMesh
{
    public readonly List<Vector3> v = new List<Vector3>();
    public readonly List<Vector3> n = new List<Vector3>();
    public readonly List<Vector2> uv = new List<Vector2>();
    /// <summary>One triangle list per material; the last is the cut faces.</summary>
    public readonly List<List<int>> sub = new List<List<int>>();

    public int CutSub => sub.Count - 1;

    public Bounds Bounds
    {
        get
        {
            if (v.Count == 0) return new Bounds();
            var b = new Bounds(v[0], Vector3.zero);
            foreach (Vector3 p in v) b.Encapsulate(p);
            return b;
        }
    }

    /// <summary>How much it holds - the closed surface's own volume (cut parts are
    /// closed by their cut faces, so the parts' volumes add up to the whole's);
    /// its bounds' if the surface isn't closed. For sharing an amount out.</summary>
    public float Volume
    {
        get
        {
            float vol = 0f;
            foreach (List<int> t in sub)
                for (int i = 0; i + 2 < t.Count; i += 3)
                    vol += Vector3.Dot(v[t[i]], Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6f;
            vol = Mathf.Abs(vol);
            Vector3 s = Bounds.size;
            float box = s.x * s.y * s.z;
            return vol > box * 0.2f && vol <= box * 1.05f ? vol : box;
        }
    }

    /// <summary>The model on mf, brought into the frame `into`.</summary>
    public static SliceMesh From(MeshFilter mf, Transform into)
    {
        var m = new SliceMesh();
        Mesh src = mf.sharedMesh;
        Vector3[] sv = src.vertices, sn = src.normals;
        Vector2[] su = src.uv;
        Transform t = mf.transform;
        for (int i = 0; i < sv.Length; i++)
        {
            m.v.Add(into.InverseTransformPoint(t.TransformPoint(sv[i])));
            m.n.Add(sn.Length == sv.Length ? into.InverseTransformDirection(t.TransformDirection(sn[i])).normalized : Vector3.up);
            m.uv.Add(su.Length == sv.Length ? su[i] : Vector2.zero);
        }
        for (int s = 0; s < src.subMeshCount; s++) m.sub.Add(new List<int>(src.GetTriangles(s)));
        m.sub.Add(new List<int>()); // (the cut faces)
        return m;
    }

    private SliceMesh EmptyLike()
    {
        var m = new SliceMesh();
        for (int s = 0; s < sub.Count; s++) m.sub.Add(new List<int>());
        return m;
    }

    /// <summary>A Mesh of it, its vertices moved by -origin (so origin becomes the pivot).</summary>
    public Mesh ToMesh(Vector3 origin, string name)
    {
        var mesh = new Mesh { name = name };
        var pv = new List<Vector3>(v.Count);
        foreach (Vector3 p in v) pv.Add(p - origin);
        mesh.SetVertices(pv);
        mesh.SetNormals(n);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = sub.Count;
        for (int s = 0; s < sub.Count; s++) mesh.SetTriangles(sub[s], s);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Cut across one axis of the frame (0 x, 1 y, 2 z) at a value: the part
    /// below it and the part above, each closed with a flat cut face. A side with
    /// nothing in it comes back null.</summary>
    public void Split(int axis, float at, out SliceMesh below, out SliceMesh above)
    {
        below = EmptyLike();
        above = EmptyLike();
        var mapB = new Dictionary<int, int>();
        var mapA = new Dictionary<int, int>();
        var ring = new List<Vector3>();

        for (int s = 0; s < sub.Count; s++)
        {
            List<int> tris = sub[s];
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int[] idx = { tris[t], tris[t + 1], tris[t + 2] };
                float[] d = { v[idx[0]][axis] - at, v[idx[1]][axis] - at, v[idx[2]][axis] - at };
                bool[] up = { d[0] >= 0f, d[1] >= 0f, d[2] >= 0f };
                int ups = (up[0] ? 1 : 0) + (up[1] ? 1 : 0) + (up[2] ? 1 : 0);
                if (ups == 3) { foreach (int i in idx) above.sub[s].Add(Map(above, mapA, i)); continue; }
                if (ups == 0) { foreach (int i in idx) below.sub[s].Add(Map(below, mapB, i)); continue; }

                // the vertex alone on its side first (cyclic order kept, so the winding is)
                int lone = ups == 1 ? (up[0] ? 0 : up[1] ? 1 : 2) : (!up[0] ? 0 : !up[1] ? 1 : 2);
                int a = idx[lone], b = idx[(lone + 1) % 3], c = idx[(lone + 2) % 3];
                float da = d[lone], db = d[(lone + 1) % 3], dc = d[(lone + 2) % 3];
                bool loneUp = up[lone];
                float tab = da / (da - db), tac = da / (da - dc);
                Vector3 pab = Vector3.Lerp(v[a], v[b], tab), pac = Vector3.Lerp(v[a], v[c], tac);
                Vector3 nab = Vector3.Lerp(n[a], n[b], tab).normalized, nac = Vector3.Lerp(n[a], n[c], tac).normalized;
                Vector2 uab = Vector2.Lerp(uv[a], uv[b], tab), uac = Vector2.Lerp(uv[a], uv[c], tac);
                ring.Add(pab);
                ring.Add(pac);

                SliceMesh loneSide = loneUp ? above : below, other = loneUp ? below : above;
                var loneMap = loneUp ? mapA : mapB;
                var otherMap = loneUp ? mapB : mapA;
                int la = Map(loneSide, loneMap, a);
                int lab = Add(loneSide, pab, nab, uab), lac = Add(loneSide, pac, nac, uac);
                loneSide.sub[s].AddRange(new[] { la, lab, lac });
                int ob = Map(other, otherMap, b), oc = Map(other, otherMap, c);
                int oab = Add(other, pab, nab, uab), oac = Add(other, pac, nac, uac);
                other.sub[s].AddRange(new[] { oab, ob, oc, oab, oc, oac });
            }
        }

        // the flat faces the cut leaves, one on each part, facing out of it
        Vector3 axisDir = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        Cap(below, ring, axis, axisDir);
        Cap(above, ring, axis, -axisDir);
        if (below.v.Count == 0) below = null;
        if (above.v.Count == 0) above = null;
    }

    private int Map(SliceMesh to, Dictionary<int, int> map, int i)
    {
        if (map.TryGetValue(i, out int j)) return j;
        j = Add(to, v[i], n[i], uv[i]);
        map[i] = j;
        return j;
    }

    private static int Add(SliceMesh to, Vector3 p, Vector3 nrm, Vector2 u)
    {
        to.v.Add(p);
        to.n.Add(nrm);
        to.uv.Add(u);
        return to.v.Count - 1;
    }

    /// <summary>The cut's outline closed with a fan from its middle (a block's cut
    /// is convex), on the cut-face submesh, its UVs laid flat on the cut.</summary>
    private static void Cap(SliceMesh m, List<Vector3> ring, int axis, Vector3 normal)
    {
        if (m.v.Count == 0 || ring.Count < 3) return;
        int ua = (axis + 1) % 3, va = (axis + 2) % 3;
        Vector3 c = Vector3.zero;
        foreach (Vector3 p in ring) c += p;
        c /= ring.Count;
        var pts = new List<Vector3>(ring);
        pts.Sort((p, q) => Mathf.Atan2(p[va] - c[va], p[ua] - c[ua]).CompareTo(Mathf.Atan2(q[va] - c[va], q[ua] - c[ua])));
        var clean = new List<Vector3>();
        foreach (Vector3 p in pts) if (clean.Count == 0 || (p - clean[clean.Count - 1]).sqrMagnitude > 1e-10f) clean.Add(p);
        if (clean.Count > 2 && (clean[0] - clean[clean.Count - 1]).sqrMagnitude <= 1e-10f) clean.RemoveAt(clean.Count - 1);
        if (clean.Count < 3) return;

        bool flip = Vector3.Dot(Vector3.Cross(clean[0] - c, clean[1] - c), normal) < 0f;
        Vector2 UV(Vector3 p) => new Vector2(p[ua], p[va]) * 3f;
        int ci = Add(m, c, normal, UV(c));
        int first = m.v.Count;
        foreach (Vector3 p in clean) Add(m, p, normal, UV(p));
        for (int i = 0; i < clean.Count; i++)
        {
            int a = first + i, b = first + (i + 1) % clean.Count;
            if (flip) m.sub[m.CutSub].AddRange(new[] { ci, b, a });
            else m.sub[m.CutSub].AddRange(new[] { ci, a, b });
        }
    }
}
