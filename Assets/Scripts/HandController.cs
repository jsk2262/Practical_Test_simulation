using System;
using UnityEngine;

public class HandController : MonoBehaviour
{
    public static HandController Instance { get; private set; }

    public float minDuration = 0.28f;
    public float speedForLongDistance = 11f;
    /// <summary>Upper bound on any single move - at speedForLongDistance alone a
    /// cross-counter reach took over a second, and a held item (which rides the
    /// hand) visibly trailed behind every click.</summary>
    [SerializeField] private float maxDuration = 0.3f;

    [SerializeField] private float curlSpeed = 9f;

    private Vector3 startPos;
    private Vector3 targetPos;
    private Action onArrive;
    private bool moving;
    private float elapsed;
    private float duration;

    private GleechiHandPoser poser;
    private float curlT;
    private Renderer[] visualRenderers;

    /// <summary>Where the hand actually rests once GoTo's walk settles -
    /// rockOffset/rockRotation (ToolItem's chop-rock) layer on top of this
    /// each frame rather than being folded directly into transform, so a
    /// later GoTo (or the settle itself) isn't thrown off by whatever offset
    /// happened to be live at the time.</summary>
    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector3 rockOffset = Vector3.zero;
    private Quaternion rockRotation = Quaternion.identity;

    void Awake()
    {
        Instance = this;

        // The hand parks itself in front of whatever was just clicked (see
        // Interactable's handOffset) and stays there until the next click -
        // its own collider would then sit directly in the raycast path to
        // that same spot, silently swallowing every further click there. The
        // hand was never meant to be clickable itself, so none of its
        // colliders should exist at runtime.
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;

        poser = GetComponentInChildren<GleechiHandPoser>(true);
        visualRenderers = GetComponentsInChildren<Renderer>(true);

        restPosition = transform.position;
        restRotation = transform.rotation;
    }

    void Update()
    {
        UpdateMovement();
        UpdateGrabPose();

        // Applied every frame (not just while rocking) so a hand-carried
        // item - whose own position is computed as this transform's position
        // plus its hold offset, read in LateUpdate right after this - always
        // sees the same combined pose the hand model itself just rendered at,
        // instead of the two drifting apart mid-gesture (reproduced during the
        // knife's chop rock: the blade rocked around its own pivot while the
        // hand mesh stayed put, so the two visibly separated).
        transform.position = restPosition + rockOffset;
        transform.rotation = restRotation * rockRotation;
    }

    /// <summary>Hides (only the renderers of) this right hand while the left
    /// hand does the holding - a pan carried by its handle. Movement, clicks
    /// and gestures keep running underneath.</summary>
    public void SetVisualHidden(bool hidden)
    {
        if (hidden)
        {
            if (hiddenRenderers != null) return;
            // Only the ones showing right now - restoring every renderer used to
            // also switch on the old placeholder hand's parts (a beige palm
            // sphere), which are meant to stay off.
            hiddenRenderers = System.Array.FindAll(visualRenderers, r => r != null && r.enabled);
            foreach (Renderer r in hiddenRenderers) r.enabled = false;
        }
        else
        {
            if (hiddenRenderers == null) return;
            foreach (Renderer r in hiddenRenderers) if (r != null) r.enabled = true;
            hiddenRenderers = null;
        }
    }

    private Renderer[] hiddenRenderers;

    /// <summary>Where the middle knuckles sit relative to the knife's grip point
    /// in the chop stance - just past the handle (and toward the camera) so it
    /// runs through the middle of the curled fist (screenshot-checked).</summary>
    [SerializeField] private Vector3 chopHandleGripOffset = new Vector3(0.12f, 0.08f, -0.3f);

    /// <summary>The knife-chopping stance, driven by ToolItem's chop motion:
    /// blend 0..1 turns the hand from its normal pose to a fist around the
    /// upright knife's handle - fingers pointing at 3 o'clock, wrapped around
    /// it, thumb up toward the blade - and offset moves it along with the
    /// knife's up/down strokes. holdSpot is where (from this transform) the
    /// knife's grip point sits; the hand is placed by its knuckles so its fist
    /// closes right there, since turning it about its own pivot would swing
    /// the fist away from the handle. Returns that extra shift (beyond offset),
    /// for the held knife to cancel out. Pass (0, zero) to release.</summary>
    public Vector3 SetChopPose(float blend, Vector3 offset, Vector3 holdSpot = default)
    {
        if (blend <= 0f)
        {
            rockOffset = offset;
            rockRotation = Quaternion.identity;
            return Vector3.zero;
        }

        if (!chopAimMeasured)
        {
            // Measured once, from the rest pose (rock cleared first so the
            // knuckles are read at exactly restRotation). Right hand, fingers
            // at 3 o'clock -> thumb side faces 12 o'clock.
            transform.rotation = restRotation;
            if (poser != null && poser.TryGetAimRotation(chopFingerDir, chopThumbSide, out Quaternion aim))
            {
                chopAimLocal = Quaternion.Inverse(restRotation) * aim * restRotation;
                transform.rotation = restRotation * chopAimLocal;
                chopKnuckleDelta = poser.KnucklePosition - transform.position;
                transform.rotation = restRotation;
            }
            chopAimMeasured = true;
        }
        rockRotation = Quaternion.Slerp(Quaternion.identity, chopAimLocal, blend);
        Vector3 fistShift = (holdSpot + chopHandleGripOffset - chopKnuckleDelta) * blend;
        rockOffset = offset + fistShift;
        return fistShift;
    }

    private Vector3 chopKnuckleDelta;

    /// <summary>Shifts the hand off its rest spot without turning it - a
    /// scripted reach (the measuring spoon dipping into a bottle). Zero releases.</summary>
    public void SetGestureOffset(Vector3 offset) => rockOffset = offset;

    /// <summary>Chop grip aim (world), the 중식도 grip: the hand on the handle's
    /// right, back of the hand up, fingers wrapping round the handle toward
    /// the left and down into the board, thumb side toward the blade.</summary>
    [SerializeField] private Vector3 chopFingerDir = new Vector3(-0.9f, -0.42f, 0.6f);
    [SerializeField] private Vector3 chopThumbSide = new Vector3(-0.42f, 0.9f, 0f);

    /// <summary>Sets the chop grip aim (re-measured on the next stroke).</summary>
    public void SetChopAim(Vector3 fingerDir, Vector3 thumbSide, Vector3 gripOffset)
    {
        chopFingerDir = fingerDir;
        chopThumbSide = thumbSide;
        chopHandleGripOffset = gripOffset;
        chopAimMeasured = false;
    }

    private bool chopAimMeasured;
    private Quaternion chopAimLocal = Quaternion.identity;

    private float kneadCurl = -1f;

    /// <summary>The hand in the bowl kneading (MeatKneader): turned so its
    /// fingers point along fingerDir (thumb side toward thumbSide), its middle
    /// knuckle at knuckleWorld, the fingers curled by curl - off its rest spot
    /// like the other gestures, so letting go puts it straight back.</summary>
    public void SetKneadPose(Vector3 knuckleWorld, Vector3 fingerDir, Vector3 thumbSide, float curl)
    {
        kneadCurl = Mathf.Clamp01(curl);
        transform.position = restPosition;
        transform.rotation = restRotation;
        rockRotation = Quaternion.identity;
        if (poser != null && poser.TryGetAimRotation(fingerDir, thumbSide, out Quaternion aim))
            rockRotation = Quaternion.Inverse(restRotation) * aim * restRotation;
        transform.rotation = restRotation * rockRotation;
        Vector3 knuckle = poser != null ? poser.KnucklePosition : transform.position;
        rockOffset = knuckleWorld - knuckle;
        transform.position = restPosition + rockOffset;
    }

    /// <summary>Back from kneading: rest pose, its own curl again.</summary>
    public void EndKneadPose()
    {
        kneadCurl = -1f;
        rockOffset = Vector3.zero;
        rockRotation = Quaternion.identity;
    }

    private void UpdateGrabPose()
    {
        if (poser == null) return;
        if (kneadCurl >= 0f)
        {
            poser.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
            poser.SetCurl(kneadCurl);
            return;
        }

        bool holding = HandCarry.Instance != null && HandCarry.Instance.Current != null;
        curlT = Mathf.MoveTowards(curlT, holding ? 1f : 0f, curlSpeed * Time.deltaTime);
        bool knifeHeld = HandCarry.Instance != null &&
            HandCarry.Instance.Current is ToolItem tool && tool.Kind == ToolKind.Knife;
        poser.SetPoseMode(knifeHeld ? GleechiHandPoser.PoseMode.KnifeGrip : GleechiHandPoser.PoseMode.Carry);
        poser.SetCurl(curlT);
    }

    /// <summary>Plays a brief scrubbing gesture on the hand model - for washing
    /// hands at the sink (empty-handed) or rinsing an ingredient under the tap.
    /// Brings the off-hand in to scrub alongside it, so this reads as two hands
    /// washing together rather than one hand alone.</summary>
    public void PlayWashGesture(float duration = 1.2f)
    {
        poser?.PlayWashGesture(duration);
        SupportHandController.Instance?.ShowWashingBeside(transform, duration);
    }

    /// <summary>Plays a single knife-stroke snap - call once per piece cut.</summary>
    public void PlayChopGesture()
    {
        poser?.PlayChopGesture();
    }

    /// <summary>Feeds the wok's current shake speed in so the hand wobbles to
    /// match - call every frame with however fast the wok is being shaken.</summary>
    public void SetHandShake(float shakeSpeed, float fullShakeThreshold)
    {
        float intensity = fullShakeThreshold > 0f ? shakeSpeed / fullShakeThreshold : 0f;
        poser?.SetShakeIntensity(intensity);
    }

    /// <summary>Where the hand rests (before any chop/gesture offset).</summary>
    public Vector3 RestPosition => restPosition;

    /// <summary>Still on its way to where it was last sent (GoTo/SlideTo).</summary>
    public bool IsMoving => moving;

    /// <summary>Slides the hand's rest spot over, no arrival action and the
    /// off-hand left where it is - the knife moving along the board from one
    /// cut line to the next.</summary>
    public void SlideTo(Vector3 worldPos, float seconds = 0.12f)
    {
        startPos = restPosition;
        targetPos = worldPos;
        onArrive = null;
        duration = Mathf.Max(seconds, 0.01f);
        elapsed = 0f;
        moving = true;
    }

    public void GoTo(Vector3 worldPos, Action action)
    {
        SupportHandController.Instance?.ReleaseParked();
        // A gesture's leftover offset (the spoon left dipped in a bowl) stays
        // behind - it belonged to that spot.
        rockOffset = Vector3.zero;

        startPos = restPosition;
        targetPos = worldPos;
        onArrive = action;

        float dist = Vector3.Distance(startPos, targetPos);
        duration = Mathf.Min(Mathf.Max(minDuration, dist / speedForLongDistance), maxDuration);
        elapsed = 0f;
        moving = true;
    }

    private void UpdateMovement()
    {
        if (!moving) return;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);
        // Ease-out (fast start, soft landing): smoothstep's slow start read as
        // the hand ignoring the click for the first ~0.1s.
        float inv = 1f - t;
        float eased = 1f - inv * inv * inv;

        restPosition = Vector3.Lerp(startPos, targetPos, eased);

        if (t >= 1f)
        {
            moving = false;
            Action callback = onArrive;
            onArrive = null;
            callback?.Invoke();
        }
    }
}
