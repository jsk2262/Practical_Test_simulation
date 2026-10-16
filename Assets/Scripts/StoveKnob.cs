using System.Collections;
using UnityEngine;

/// <summary>
/// The physical stove dial - clicked to step through its settings, one click
/// at a time: off -> strong -> medium -> weak -> off, same as a real gas valve
/// that opens fully first and gets dialed down click by click. Off is the
/// lever straight up (as placed); strong turns it 180 degrees to the left,
/// medium 120, weak 60. The knob turns in place about its own center (around
/// the view axis), and the hand reaches over to the lever to do it.
/// </summary>
public class StoveKnob : MonoBehaviour, IClickable
{
    [SerializeField] private StoveBurner burner;
    [SerializeField] private float turnSeconds = 0.2f;

    /// <summary>이 손잡이가 켜고 끄는 화구 (화구 확대 구도에 함께 담는다 - KitchenCamera).</summary>
    public StoveBurner Burner => burner;

    [Header("Split knob (Tools > Kitchen > Split Stove Knob Lever)")]
    /// <summary>Just the lever part of the knob - only it turns; the knob body stays put.</summary>
    [SerializeField] private Transform lever;
    /// <summary>The knob's shaft and center, in its own model space.</summary>
    [SerializeField] private Vector3 shaftLocal = Vector3.up;
    [SerializeField] private Vector3 pivotLocal;
    /// <summary>Where the hand goes relative to the knob's center when turning it.</summary>
    [SerializeField] private Vector3 handOffset = new Vector3(0f, 0f, -0.55f);

    private const float OffAngle = 0f;
    private const float WeakAngle = 60f;
    private const float MediumAngle = 120f;
    private const float StrongAngle = 180f;

    private float angle;      // the setting
    private float shownAngle; // what the model currently shows (animates toward angle)
    private Quaternion baseRotation;
    private Vector3 basePosition;
    private Vector3 pivot;
    private Vector3 axis;
    private bool baseCaptured;
    private Coroutine turning;

    void Awake()
    {
        CaptureBase();
    }

    /// <summary>The knob was hand-placed with its own rest tilt (an extracted FBX
    /// part, pivot not at the knob's center) - it turns from THAT pose, about its
    /// own visual center, so it spins in place instead of swinging around.</summary>
    private void CaptureBase()
    {
        if (baseCaptured) return;
        baseRotation = transform.rotation;
        basePosition = transform.position;
        Renderer r = GetComponentInChildren<Renderer>();
        pivot = r != null ? r.bounds.center : transform.position;

        // Turned about the view axis through the knob's center: on screen only the
        // lever's angle changes ("left" = counter-clockwise = positive about +Z).
        // (The model's pivot is off its center - turning about that swung the
        // whole knob around; its own tilted axes made it tumble.)
        axis = Vector3.forward;
        baseCaptured = true;
    }

    public void OnClicked()
    {
        // The grate off the burner (being cleaned under): not lit (turning it off is fine).
        if (burner != null && angle <= OffAngle + 0.01f && BurnerGrate.IsDetached(burner))
        {
            BurnerGrate.For(burner).Say("받침을 올린 뒤에 불을 켜세요");
            return;
        }
        CaptureBase();
        angle = NextAngle(angle);

        HandController.Instance?.GoTo(pivot + handOffset, null);
        if (turning != null) StopCoroutine(turning);
        turning = StartCoroutine(TurnTo(angle));
        ApplyFlame();
    }

    private static float NextAngle(float current)
    {
        if (current <= OffAngle + 0.01f) return StrongAngle;
        if (current >= StrongAngle - 0.01f) return MediumAngle;
        if (current >= MediumAngle - 0.01f) return WeakAngle;
        return OffAngle;
    }

    private IEnumerator TurnTo(float target)
    {
        float from = shownAngle;
        for (float t = 0f; t < turnSeconds; t += Time.deltaTime)
        {
            ShowAngle(Mathf.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / turnSeconds)));
            yield return null;
        }
        ShowAngle(target);
        turning = null;
    }

    private void ShowAngle(float a)
    {
        shownAngle = a;
        if (lever != null)
        {
            // Lever only, about the knob's own shaft through its center. "Left" is
            // counter-clockwise as seen from the camera, whichever way the shaft points.
            float sign = Vector3.Dot(transform.TransformDirection(shaftLocal), Vector3.forward) >= 0f ? 1f : -1f;
            Quaternion r = Quaternion.AngleAxis(a * sign, shaftLocal);
            lever.localRotation = r;
            lever.localPosition = pivotLocal - r * pivotLocal;
            return;
        }
        transform.SetPositionAndRotation(basePosition, baseRotation);
        transform.RotateAround(pivot, axis, a);
    }

    private void ApplyFlame()
    {
        if (burner == null) return;

        if (angle <= OffAngle + 0.01f)
        {
            burner.SetOn(false);
            return;
        }

        StoveBurner.FlameLevel level =
            angle <= WeakAngle + 0.01f ? StoveBurner.FlameLevel.Weak :
            angle <= MediumAngle + 0.01f ? StoveBurner.FlameLevel.Medium :
            StoveBurner.FlameLevel.Strong;

        burner.SetLevel(level);
        burner.SetOn(true);
    }
}
