using UnityEngine;

/// <summary>
/// The "Pieces" child a cut ingredient keeps its cut pieces under when its own
/// scale isn't even (the cucumber portion: 0.6 x 1.08 x 1.08): it cancels that
/// scale every frame, so pieces turned every which way under it keep their
/// shapes - parented straight to the ingredient they came out skewed.
/// </summary>
public class PieceHolder : MonoBehaviour
{
    public static Transform For(Transform ingredient)
    {
        Transform t = ingredient.Find("Pieces");
        if (t == null)
        {
            t = new GameObject("Pieces").transform;
            t.SetParent(ingredient, false);
            t.gameObject.AddComponent<PieceHolder>();
        }
        t.GetComponent<PieceHolder>()?.Fix();
        return t;
    }

    void LateUpdate() => Fix();

    public void Fix()
    {
        Transform p = transform.parent;
        if (p == null) return;
        Vector3 s = p.lossyScale;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(s.x), 1e-5f), 1f / Mathf.Max(Mathf.Abs(s.y), 1e-5f), 1f / Mathf.Max(Mathf.Abs(s.z), 1e-5f));
    }
}
