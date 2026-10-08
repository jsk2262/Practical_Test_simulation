using UnityEngine;

/// <summary>
/// The faucet's lever handle, following its WaterTap: lowered while the water
/// runs, raised while it's off. Sits on a pivot at the lever's back end (where
/// it joins the faucet body) and only turns that pivot - the click itself is
/// the tap's own collider, placed over the lever (Tools > Kitchen > Setup
/// Faucet Lever).
/// </summary>
public class FaucetLever : MonoBehaviour
{
    [SerializeField] private WaterTap tap;
    [SerializeField] private Vector3 axis = Vector3.right;
    [SerializeField] private float onAngle = 0f;
    [SerializeField] private float offAngle = 30f;
    [SerializeField] private float speed = 10f;

    private Quaternion rest;
    private float angle;

    void Awake()
    {
        rest = transform.localRotation;
        angle = tap != null && tap.IsOn ? onAngle : offAngle;
        Apply();
    }

    void Update()
    {
        if (tap == null) return;
        float target = tap.IsOn ? onAngle : offAngle;
        angle = Mathf.Lerp(angle, target, 1f - Mathf.Exp(-speed * Time.deltaTime));
        Apply();
    }

    private void Apply() => transform.localRotation = rest * Quaternion.AngleAxis(angle, axis);
}
