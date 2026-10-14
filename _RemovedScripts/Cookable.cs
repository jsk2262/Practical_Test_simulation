using UnityEngine;

public class Cookable : MonoBehaviour
{
    [SerializeField] private StoveBurner burner;
    [SerializeField] private float timeToCooked = 6f;
    [SerializeField] private float timeToBurnt = 16f;

    private static readonly Color CookedColor = new Color(0.45f, 0.28f, 0.15f, 1f);
    private static readonly Color BurntColor = new Color(0.08f, 0.08f, 0.08f, 1f);

    private float heatTime;
    private Renderer rend;
    private Color rawColor;

    public enum CookState { Raw, Cooking, Cooked, Burnt }
    public CookState State { get; private set; } = CookState.Raw;

    void Start()
    {
        rend = GetComponent<Renderer>();
        rawColor = rend.sharedMaterial.color;
        rend.material = new Material(rend.sharedMaterial);
    }

    void Update()
    {
        if (burner == null || !burner.IsOn)
        {
            return;
        }

        heatTime += Time.deltaTime;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        Color target;

        if (heatTime < timeToCooked)
        {
            State = heatTime > 0.01f ? CookState.Cooking : CookState.Raw;
            float t = heatTime / timeToCooked;
            target = Color.Lerp(rawColor, CookedColor, t);
        }
        else if (heatTime < timeToBurnt)
        {
            State = CookState.Cooked;
            float t = (heatTime - timeToCooked) / (timeToBurnt - timeToCooked);
            target = Color.Lerp(CookedColor, BurntColor, t);
        }
        else
        {
            State = CookState.Burnt;
            target = BurntColor;
        }

        rend.material.color = target;
    }
}
