using UnityEngine;

public class Blanchable : MonoBehaviour
{
    [SerializeField] private StoveBurner burner;
    [SerializeField] private float timeToBlanched = 4f;

    private static readonly Color BlanchedColor = new Color(0.35f, 0.75f, 0.3f, 1f);

    private float heatTime;
    private Renderer rend;
    private Color rawColor;

    public bool IsBlanched { get; private set; }

    void Start()
    {
        rend = GetComponent<Renderer>();
        rawColor = rend.sharedMaterial.color;
        rend.material = new Material(rend.sharedMaterial);
    }

    void Update()
    {
        if (IsBlanched || burner == null || !burner.IsOn) return;

        heatTime += Time.deltaTime;
        float t = Mathf.Clamp01(heatTime / timeToBlanched);
        rend.material.color = Color.Lerp(rawColor, BlanchedColor, t);

        if (heatTime >= timeToBlanched)
        {
            IsBlanched = true;
        }
    }
}
