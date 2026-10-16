using System.Collections;
using UnityEngine;

/// <summary>
/// Poses the Gleechi rigged hand mesh's finger bones directly - built without any
/// dependency on the VirtualGrasp SDK's own VR-controller-driven grasp system
/// (VG_Controller/VG_FingerAnimator), since this project is mouse-driven, not VR.
/// The SDK itself is left untouched in the project in case a VR build is ever
/// added later; this only borrows the hand mesh/rig it ships with.
///
/// All 5 fingers on this rig curl around each bone's own local +Z axis - found by
/// testing all 6 axis/sign combinations and picking whichever brought every
/// fingertip closest to the palm when rotating proximal+middle+distal together.
///
/// Three distinct action gestures share the same finger-curl building block:
/// - SetCurl: the base "holding something" grip, and what carrying an
///   ingredient/tool between spots (재료 옮기기) looks like on its own.
/// - PlayWashGesture: a slow rub, for washing hands and rinsing ingredients.
/// - PlayChopGesture: a single quick snap, one per knife stroke.
/// - SetShakeIntensity: a continuous fast wobble driven every frame by however
///   hard the wok is currently being shaken.
/// </summary>
public class GleechiHandPoser : MonoBehaviour
{
    [SerializeField] private float maxCurlDegrees = 75f;

    /// <summary>Carry = the uniform SetCurl grip (and the wash/chop/shake
    /// gestures built on it); KnifeGrip / SupportClaw = per-joint poses for
    /// wrapping a knife handle / the fingertips-tucked hand steadying an
    /// ingredient, which a single shared curl angle can't make (every joint
    /// bent the same way just reads as a fist).</summary>
    public enum PoseMode { Carry, KnifeGrip, SupportClaw, ChopstickGrip }

    [System.Serializable]
    public class FingerAngles
    {
        public Vector3 proximal, middle, distal;
        public FingerAngles(float a, float b, float c)
        {
            proximal = new Vector3(0, 0, a);
            middle = new Vector3(0, 0, b);
            distal = new Vector3(0, 0, c);
        }
    }

    // Starting points for this rig's +Z curl, not a calibrated pose - thumb
    // opposition and mirrored rigs need Inspector tuning.
    [Header("Per-joint local Euler offsets: Thumb, Index, Middle, Ring, Pinky")]
    [SerializeField] private FingerAngles[] knifeGrip = {
        new FingerAngles(20, 25, 15), new FingerAngles(35, 55, 30),
        new FingerAngles(45, 65, 35), new FingerAngles(50, 70, 40),
        new FingerAngles(55, 70, 40)
    };
    [SerializeField] private FingerAngles[] supportClaw = {
        new FingerAngles(25, 35, 20), new FingerAngles(25, 75, 50),
        new FingerAngles(20, 80, 55), new FingerAngles(25, 80, 50),
        new FingerAngles(30, 75, 45)
    };
    [SerializeField] private float poseResponse = 14f;
    private PoseMode poseMode;
    private float chopstickClose;
    private Vector3 visualRestPosition;
    private Quaternion visualRestRotation;
    private float utensilWristAngle;
    private bool wristWasPosed;
    public void SetUtensilWrist(float degrees) => utensilWristAngle = degrees;
    public void SetChopstickClose(float value) => chopstickClose = Mathf.Clamp01(value);

    [SerializeField] private FingerAngles[] chopstickGrip = {
        new FingerAngles(15, 22, 12) { proximal = new Vector3(-15, 25, 15) },
        new FingerAngles(18, 40, 18), new FingerAngles(28, 48, 25),
        new FingerAngles(48, 72, 42), new FingerAngles(55, 78, 48)
    };

    public void SetPoseMode(PoseMode mode) { poseMode = mode; }

    void LateUpdate()
    {
        bool wristActive = Mathf.Abs(utensilWristAngle) > 0.01f;
        if (wristActive || wristWasPosed)
        {
            transform.localPosition = visualRestPosition;
            transform.localRotation = visualRestRotation;
        }
        if (wristActive)
            transform.RotateAround(KnucklePosition, Vector3.forward, utensilWristAngle);
        wristWasPosed = wristActive;
        if (fingers == null || poseMode == PoseMode.Carry) return;
        FingerAngles[] angles = poseMode == PoseMode.ChopstickGrip ? chopstickGrip : poseMode == PoseMode.KnifeGrip ? knifeGrip : supportClaw;
        if (angles == null || angles.Length != fingers.Length) return;

        float blend = 1f - Mathf.Exp(-Mathf.Max(0f, poseResponse) * Time.deltaTime);
        for (int i = 0; i < fingers.Length; i++)
        {
            Finger f = fingers[i];
            FingerAngles a = angles[i];
            if (a == null) continue;
            Vector3 movingFinger = poseMode == PoseMode.ChopstickGrip && (i == 1 || i == 2)
                ? new Vector3(0, 0, chopstickClose * 12f) : Vector3.zero;
            f.proximal.localRotation = Quaternion.Slerp(f.proximal.localRotation, f.restProximal * Quaternion.Euler(a.proximal), blend);
            f.middle.localRotation = Quaternion.Slerp(f.middle.localRotation, f.restMiddle * Quaternion.Euler(a.middle + movingFinger), blend);
            f.distal.localRotation = Quaternion.Slerp(f.distal.localRotation, f.restDistal * Quaternion.Euler(a.distal), blend);
        }
    }

    private struct Finger
    {
        public Transform proximal, middle, distal;
        public Quaternion restProximal, restMiddle, restDistal;
    }

    private Finger[] fingers;
    private Transform wrist;
    private float currentCurl;
    private float shakeIntensity;
    private Coroutine overrideRoutine;

    void Awake()
    {
        visualRestPosition = transform.localPosition;
        visualRestRotation = transform.localRotation;
        wrist = transform.Find("GleechiHandRig/Neck/Clavicle_L/Shoulder_L/Elbow_L/Wrist_L");
        if (wrist == null)
        {
            enabled = false;
            return;
        }

        string[] names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        fingers = new Finger[names.Length];

        for (int i = 0; i < names.Length; i++)
        {
            Transform a = wrist.Find(names[i] + "A_L");
            Transform b = a != null ? a.Find(names[i] + "B_L") : null;
            Transform c = b != null ? b.Find(names[i] + "C_L") : null;
            if (a == null || b == null || c == null)
            {
                Debug.LogError("Hand pose: missing finger chain " + names[i], this);
                fingers = null;
                enabled = false;
                return;
            }

            fingers[i] = new Finger
            {
                proximal = a,
                middle = b,
                distal = c,
                restProximal = a.localRotation,
                restMiddle = b.localRotation,
                restDistal = c.localRotation,
            };
        }
    }

    void Update()
    {
        // Continuous wobble on top of whatever the base pose currently is -
        // decays back to 0 on its own as WokDragHandler's own ShakeSpeed does,
        // so nothing needs to explicitly turn it back off.
        if (shakeIntensity > 0.01f && overrideRoutine == null)
        {
            float wobble = Mathf.Sin(Time.time * 28f) * 0.5f + 0.5f;
            ApplyCurl(Mathf.Clamp01(currentCurl + wobble * shakeIntensity * 0.4f));
        }
    }

    /// <summary>The world rotation that turns this hand so its fingers point
    /// along fingerTarget and its thumb side faces thumbSideTarget, measured
    /// from the rig's own knuckles (wrist -> middle knuckle = finger direction,
    /// pinky knuckle -> index knuckle = thumb side). Aiming by the rig itself
    /// instead of hand-tuned Euler angles means the palm/back-of-hand side
    /// comes out right automatically (fixed by which way the thumb goes), for
    /// either hand and whatever rotation the model was authored at.</summary>
    /// <summary>Middle-finger knuckle - a stable "where the hand is" point for
    /// placing the hand by its knuckles rather than its model origin.</summary>
    public Vector3 KnucklePosition => fingers != null ? fingers[2].proximal.position : transform.position;

    public bool TryGetAimRotation(Vector3 fingerTarget, Vector3 thumbSideTarget, out Quaternion delta)
    {
        delta = Quaternion.identity;
        if (fingers == null || wrist == null) return false;

        Vector3 fingerDir = fingers[2].proximal.position - wrist.position;
        Vector3 thumbSide = fingers[1].proximal.position - fingers[4].proximal.position;
        if (fingerDir.sqrMagnitude < 1e-8f || thumbSide.sqrMagnitude < 1e-8f) return false;

        Quaternion current = Quaternion.LookRotation(fingerDir, thumbSide);
        Quaternion target = Quaternion.LookRotation(fingerTarget, thumbSideTarget);
        delta = target * Quaternion.Inverse(current);
        return true;
    }

    /// <summary>0 = fully open, 1 = fully curled (loose fist) - the base grip
    /// used while carrying an ingredient/tool from one spot to another, and the
    /// pose every other gesture here returns to when it finishes.</summary>
    public void SetCurl(float t)
    {
        currentCurl = Mathf.Clamp01(t);
        if (overrideRoutine == null && shakeIntensity <= 0.01f) ApplyCurl(currentCurl);
    }

    /// <summary>0 = still, 1 = shaking as hard as the wok detects - call every
    /// frame with WokDragHandler.ShakeSpeed normalized against its own
    /// threshold; this fades the wobble back out on its own as that decays.</summary>
    public void SetShakeIntensity(float intensity01)
    {
        shakeIntensity = Mathf.Clamp01(intensity01);
    }

    private void ApplyCurl(float t)
    {
        // A per-joint pose owns the fingers while active - e.g. no fist-snap
        // per knife stroke while the hand holds the knife's grip shape.
        if (fingers == null || poseMode != PoseMode.Carry) return;

        Quaternion delta = Quaternion.AngleAxis(t * maxCurlDegrees, Vector3.forward);

        for (int i = 0; i < fingers.Length; i++)
        {
            Finger f = fingers[i];
            f.proximal.localRotation = f.restProximal * delta;
            f.middle.localRotation = f.restMiddle * delta;
            f.distal.localRotation = f.restDistal * delta;
        }
    }

    /// <summary>A brief scrubbing motion (fingers rubbing together, curl
    /// oscillating) - used for both washing hands at the sink and rinsing an
    /// ingredient under the tap.</summary>
    public void PlayWashGesture(float duration = 1.2f)
    {
        StartOverride(WashRoutine(duration));
    }

    private IEnumerator WashRoutine(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float rub = (Mathf.Sin(t * 14f) + 1f) * 0.5f;
            ApplyCurl(Mathf.Lerp(0.25f, 0.75f, rub));
            yield return null;
        }
    }

    /// <summary>A single quick snap-closed-then-relax, timed to one knife
    /// stroke - called once per piece as PerformCut works through a batch.</summary>
    public void PlayChopGesture()
    {
        StartOverride(ChopRoutine());
    }

    private IEnumerator ChopRoutine()
    {
        const float duration = 0.16f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float phase = t / duration;
            float pulse = phase < 0.4f
                ? Mathf.Lerp(currentCurl, 1f, phase / 0.4f)
                : Mathf.Lerp(1f, currentCurl, (phase - 0.4f) / 0.6f);
            ApplyCurl(pulse);
            yield return null;
        }
    }

    private void StartOverride(IEnumerator routine)
    {
        if (overrideRoutine != null) StopCoroutine(overrideRoutine);
        overrideRoutine = StartCoroutine(RunOverride(routine));
    }

    private IEnumerator RunOverride(IEnumerator routine)
    {
        yield return StartCoroutine(routine);
        overrideRoutine = null;
        ApplyCurl(currentCurl);
    }
}
