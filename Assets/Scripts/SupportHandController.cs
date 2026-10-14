using System.Collections;
using UnityEngine;

/// <summary>
/// The off-hand (left) - normally hidden, appears near an ingredient to hold it
/// steady during a two-handed action like knife cutting, or beside the primary
/// hand to wash together, while the primary (right, cursor-following) hand does
/// the actual work. Purely a visual prop: it isn't clickable and doesn't
/// participate in HandCarry.
/// </summary>
public class SupportHandController : MonoBehaviour
{
    public static SupportHandController Instance { get; private set; }

    [SerializeField] private Vector3 holdOffset = new Vector3(-0.55f, -0.1f, -0.05f);
    [SerializeField] private Vector3 washBesideOffset = new Vector3(-0.55f, 0f, -0.05f);
    [SerializeField] private float gripCurl = 0.5f;
    /// <summary>Where this hand sits relative to the (hidden) primary hand
    /// while gripping a pan's handle - zero puts it exactly where the right
    /// hand would be, i.e. its palm on the usual holding spot.</summary>
    [SerializeField] private Vector3 handleGripOffset = Vector3.zero;
    [SerializeField] private float handleGripCurl = 0.8f;
    [Header("Cutting stance")]
    /// <summary>Where the middle knuckle sits relative to the ingredient.</summary>
    [SerializeField] private Vector3 cutKnuckleOffset = new Vector3(-0.35f, 0.05f, -0.05f);
    /// <summary>Finger direction on screen, degrees from 3 o'clock (30 = about 2 o'clock).</summary>
    [SerializeField] private float cutFingerAngle = 30f;
    /// <summary>Claw grip - fingertips tucked, knuckles guiding the blade.</summary>
    [SerializeField] private float cutCurl = 0.7f;
    [SerializeField] private bool cutPalmUp;

    private Quaternion restRotation;

    private GleechiHandPoser poser;
    private GameObject visual;
    private Coroutine autoHideRoutine;
    private Transform followTarget;
    private Vector3 followOffset;

    void Awake()
    {
        Instance = this;
        poser = GetComponentInChildren<GleechiHandPoser>(true);
        restRotation = transform.rotation;

        // The root itself must stay active for this Awake() (and thus
        // Instance) to run at all - only the visual child toggles, so
        // ShowNear()/Hide() never depend on this object already being active.
        if (poser != null) visual = poser.gameObject;
        visual?.SetActive(false);
    }

    void LateUpdate()
    {
        // The primary hand keeps cosmetically traveling toward wherever was
        // just clicked (HandController.GoTo, kicked off right after the click
        // that started this wash) - a one-time position snapshot taken at the
        // start of the gesture would freeze this hand behind while the primary
        // hand walks away, so it re-anchors every frame instead.
        if (followTarget != null)
        {
            transform.position = followTarget.position + followOffset;
        }
    }

    /// <summary>Takes over holding a pan by its handle (see Carryable's
    /// holdByHandle): fist closed around the handle's end - which sits at
    /// gripOffset from the primary hand - with the thumb pointing back along
    /// the handle toward the pan. Follows the primary hand every frame until
    /// Hide().</summary>
    public void ShowGripping(Transform primaryHand, Vector3 gripOffset, Vector2 handleDir)
    {
        CancelAutoHide();
        transform.rotation = restRotation;
        visual?.SetActive(true);
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
        poser?.SetCurl(handleGripCurl);

        // Fingers wrap over the handle toward the camera; knuckles on its end.
        Vector3 knuckleDelta = Vector3.zero;
        Vector3 thumbSide = -(Vector3)handleDir;
        Vector3 fingerDir = new Vector3(0f, -1f, -0.6f);
        if (poser != null && poser.TryGetAimRotation(fingerDir, thumbSide, out Quaternion aim))
        {
            transform.rotation = aim * transform.rotation;
            knuckleDelta = poser.KnucklePosition - transform.position;
        }

        followTarget = primaryHand;
        followOffset = gripOffset + handleGripOffset - knuckleDelta;
        transform.position = primaryHand.position + followOffset;

        // Gripping something other than the (hidden) right hand = parked on a
        // pot left in place; it lets go once the right hand heads off elsewhere.
        bool onRightHand = HandController.Instance != null && primaryHand == HandController.Instance.transform;
        parkedFrame = onRightHand ? -1 : Time.frameCount;
    }

    private int parkedFrame = -1;

    /// <summary>The left hand carrying a plate or bowl in place of the right
    /// (hidden meanwhile): palm under it, right where the right hand would be,
    /// following it every frame until Hide().</summary>
    public void ShowCarrying(Transform primaryHand)
    {
        CancelAutoHide();
        parkedFrame = -1;
        transform.rotation = restRotation;
        visual?.SetActive(true);
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
        poser?.SetCurl(carryCurl);
        followTarget = primaryHand;
        followOffset = carryOffset;
        transform.position = primaryHand.position + followOffset;
    }

    [Header("Carrying a plate/bowl")]
    [SerializeField] private Vector3 carryOffset = Vector3.zero;
    [SerializeField] private float carryCurl = 0.6f;

    /// <summary>Called as the right hand sets off somewhere: a left hand parked
    /// on a pot left in place (the sink) lets go - unless it was parked by this
    /// same click.</summary>
    public void ReleaseParked()
    {
        if (parkedFrame < 0 || parkedFrame == Time.frameCount) return;
        Hide();
    }

    /// <summary>Shows this hand turned so its fingers point along fingerDir
    /// with its thumb side toward thumbSide, curled by curl - then place it
    /// frame by frame with HoldKnucklesAt (e.g. wrapped around a seasoning
    /// bottle while the right hand scoops from it). Stays until Hide().</summary>
    public void BeginHolding(Vector3 fingerDir, Vector3 thumbSide, float curl)
    {
        CancelAutoHide();
        followTarget = null;
        transform.rotation = restRotation;
        visual?.SetActive(true);
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
        poser?.SetCurl(curl);

        knuckleDelta = Vector3.zero;
        if (poser != null && poser.TryGetAimRotation(fingerDir, thumbSide, out Quaternion aim))
        {
            transform.rotation = aim * transform.rotation;
            knuckleDelta = poser.KnucklePosition - transform.position;
        }
    }

    /// <summary>Moves the hand so its middle knuckle sits at the given point.</summary>
    public void HoldKnucklesAt(Vector3 worldPoint) => transform.position = worldPoint - knuckleDelta;

    /// <summary>Curls the fingers while held (kneading - MeatKneader).</summary>
    public void SetCurl(float curl) => poser?.SetCurl(curl);

    private Vector3 knuckleDelta;

    /// <summary>Shows the support hand holding steady just beside the given
    /// world position (e.g. an ingredient on the cutting board). Stays until
    /// Hide() is called explicitly.</summary>
    public void ShowNear(Vector3 worldPos)
    {
        CancelAutoHide();
        followTarget = null;
        transform.rotation = restRotation;
        transform.position = worldPos + holdOffset;
        visual?.SetActive(true);
        // Fingertips tucked (claw) - per-joint, not the uniform curl, which reads as a fist.
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.SupportClaw);
        poser?.SetCurl(cutCurl);

        // Cutting stance (the claw grip from the reference): back of the hand
        // up, fingers curled and pointing toward about 2 o'clock with their tips
        // against the blade's side. For a left hand, back up, the thumb side is
        // the finger direction turned a quarter clockwise; cutPalmUp flips it.
        float a = cutFingerAngle * Mathf.Deg2Rad;
        Vector3 fingerDir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
        Vector3 thumbSide = new Vector3(fingerDir.y, -fingerDir.x, 0f);
        if (cutPalmUp) thumbSide = -thumbSide;
        if (poser != null && poser.TryGetAimRotation(fingerDir, thumbSide, out Quaternion aim))
        {
            transform.rotation = aim * transform.rotation;
            // Knuckles just left of the ingredient, so the fingers rest on it.
            transform.position += (worldPos + cutKnuckleOffset) - poser.KnucklePosition;
        }
    }

    /// <summary>Brings the off-hand in right beside the primary hand and rubs
    /// it through the same wash motion, so washing hands/rinsing an ingredient
    /// reads as two hands scrubbing together instead of one hand alone - then
    /// hides itself again once the gesture finishes. Keeps tracking
    /// primaryHand's transform every frame (see LateUpdate) rather than just
    /// its position at this instant, since the primary hand is usually still
    /// mid-travel toward this same click when the gesture starts.</summary>
    public void ShowWashingBeside(Transform primaryHand, float duration)
    {
        CancelAutoHide();
        followTarget = primaryHand;
        followOffset = washBesideOffset;
        transform.rotation = restRotation;
        transform.position = primaryHand.position + followOffset;
        visual?.SetActive(true);
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
        poser?.PlayWashGesture(duration);
        autoHideRoutine = StartCoroutine(HideAfter(duration));
    }

    private IEnumerator HideAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        autoHideRoutine = null;
        Hide();
    }

    private void CancelAutoHide()
    {
        if (autoHideRoutine == null) return;
        StopCoroutine(autoHideRoutine);
        autoHideRoutine = null;
    }

    public void Hide()
    {
        CancelAutoHide();
        followTarget = null;
        parkedFrame = -1;
        poser?.SetPoseMode(GleechiHandPoser.PoseMode.Carry);
        visual?.SetActive(false);
    }
}
