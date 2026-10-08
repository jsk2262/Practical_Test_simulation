using UnityEngine;

/// <summary>
/// The white arc that fills around the cursor while a press is being held
/// (나이나이 레시피 style) - ClickDetector shows it while a tool is being
/// press-and-held to pick up or put down. Drawn just in front of the camera,
/// so nothing covers it.
/// </summary>
public class HoldProgressRing : MonoBehaviour
{
    private static HoldProgressRing instance;

    private const int Segments = 48;
    [SerializeField] private float radiusPixels = 34f;
    [SerializeField] private float widthPixels = 4f;

    private LineRenderer line;

    public static void Show(Camera cam, Vector2 screenPos, float progress)
    {
        if (cam == null) return;
        if (instance == null)
        {
            var go = new GameObject("HoldProgressRing");
            instance = go.AddComponent<HoldProgressRing>();
        }
        instance.Draw(cam, screenPos, Mathf.Clamp01(progress));
    }

    public static void Hide()
    {
        if (instance != null) instance.line.enabled = false;
    }

    void Awake()
    {
        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.numCapVertices = 4;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = Color.white;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sortingOrder = 1000;
        line.enabled = false;
    }

    private void Draw(Camera cam, Vector2 screen, float progress)
    {
        if (progress <= 0f) { line.enabled = false; return; }
        float depth = cam.nearClipPlane + 0.5f;
        Vector3 c = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        float unitsPerPixel = (cam.ScreenToWorldPoint(new Vector3(screen.x + 100f, screen.y, depth)) - c).magnitude / 100f;
        float r = radiusPixels * unitsPerPixel;
        line.widthMultiplier = widthPixels * unitsPerPixel;

        int n = Mathf.Max(2, Mathf.CeilToInt(Segments * progress) + 1);
        line.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            // From 12 o'clock, clockwise.
            float a = Mathf.PI * 0.5f - (i / (float)(n - 1)) * progress * Mathf.PI * 2f;
            line.SetPosition(i, c + (cam.transform.right * Mathf.Cos(a) + cam.transform.up * Mathf.Sin(a)) * r);
        }
        line.enabled = true;
    }
}
