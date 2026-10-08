using UnityEngine;

/// <summary>
/// The white arrow that bobs over a serving plate while a pan of finished food
/// is in hand (as in the reference: "this goes here") - clicking it (or the
/// plate itself) tips the food onto that plate. Built and shown by
/// FryingPanPlating; a world-space object, so no Canvas is needed.
/// </summary>
public class PlatingArrow : MonoBehaviour, IClickable
{
    public FryingPanPlating pan;
    public PlatingDestination plate;
    /// <summary>Anything else the arrow stands for (e.g. pouring water out at the sink).</summary>
    public System.Action onClick;

    [SerializeField] private float bobHeight = 0.12f;
    [SerializeField] private float bobSpeed = 4f;

    private Vector3 basePos;

    public void Place(Vector3 world) { basePos = world; transform.position = world; }

    void Update()
    {
        transform.position = basePos + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * 0.5f + 0.5f) * bobHeight;
    }

    public void OnClicked()
    {
        if (onClick != null) onClick();
        else if (pan != null && plate != null) pan.PourInto(plate);
    }

    /// <summary>A bare arrow doing onClick when clicked (hidden until placed and shown).</summary>
    public static PlatingArrow Create(string name, System.Action onClick)
    {
        PlatingArrow arrow = Build(name);
        arrow.onClick = onClick;
        return arrow;
    }

    /// <summary>A downward arrow (point at the bottom), white with a dark rim,
    /// facing the camera.</summary>
    public static PlatingArrow Create(FryingPanPlating pan, PlatingDestination plate)
    {
        PlatingArrow arrow = Build("PlatingArrow_" + plate.name);
        arrow.pan = pan;
        arrow.plate = plate;
        return arrow;
    }

    private static PlatingArrow Build(string name)
    {
        var go = new GameObject(name);
        var arrow = go.AddComponent<PlatingArrow>();
        AddShape(go.transform, "Rim", 1.18f, new Color(0.15f, 0.15f, 0.15f), 0.01f);
        AddShape(go.transform, "Fill", 1f, Color.white, 0f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(0.5f, 0.75f, 0.1f);
        box.center = new Vector3(0f, 0.12f, 0f);
        go.SetActive(false);
        return arrow;
    }

    private static void AddShape(Transform parent, string name, float scale, Color color, float zBack)
    {
        // Head (triangle pointing down) + shaft, in the XY plane facing -Z.
        Vector3[] v =
        {
            new Vector3(-0.22f, 0.12f, 0f), new Vector3(0.22f, 0.12f, 0f), new Vector3(0f, -0.2f, 0f),
            new Vector3(-0.08f, 0.12f, 0f), new Vector3(0.08f, 0.12f, 0f), new Vector3(0.08f, 0.45f, 0f), new Vector3(-0.08f, 0.45f, 0f),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i] * scale + new Vector3(0f, 0f, zBack) + Vector3.down * (scale - 1f) * 0.05f;
        var mesh = new Mesh { vertices = v, triangles = new[] { 0, 1, 2, 3, 5, 4, 3, 6, 5 } };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var m = new Material(sh) { color = color };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
