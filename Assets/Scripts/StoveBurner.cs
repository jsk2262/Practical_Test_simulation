using UnityEngine;

public class StoveBurner : Interactable, ISlotContainer
{
    public enum FlameLevel { Weak, Medium, Strong }

    [SerializeField] private GameObject flameWeak;
    [SerializeField] private GameObject flameMedium;
    [SerializeField] private GameObject flameStrong;

    /// <summary>How far up (on screen) a vessel sits above the grate's center, so
    /// the flame under it - its size telling 강/중/약 - stays in view.</summary>
    [SerializeField] private float vesselLift = 0.35f;

    public bool IsOn { get; private set; }
    public FlameLevel Level { get; private set; } = FlameLevel.Medium;

    /// <summary>Every burner in the scene (e.g. for 겨자 fermenting near a lit one).</summary>
    public static readonly System.Collections.Generic.List<StoveBurner> All = new System.Collections.Generic.List<StoveBurner>();
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>The pot/pan/wok currently sitting on this burner, if any - a
    /// burner only ever holds one vessel at a time.</summary>
    public MovableBowl CurrentVessel { get; private set; }

    public override bool CanAcceptDrop(Carryable item)
    {
        if (CurrentVessel != null) return (object)CurrentVessel != item && CurrentVessel.CanAcceptDrop(item);
        return item is MovableBowl;
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;

        // A pot sits on the burner at the burner's own position, so the
        // burner's collider can end up in front of the pot's - a click meant
        // for the pot (dropping peas into the water, scooping with the
        // strainer...) lands here instead. Hand it on to the pot.
        if (CurrentVessel != null && hand?.Current != null && (object)hand.Current != CurrentVessel)
        {
            CurrentVessel.ForceActivate();
            return;
        }

        if (hand?.Current is MovableBowl vessel && CurrentVessel == null)
        {
            // The grate taken off for cleaning: nothing to set a pot/pan on.
            if (BurnerGrate.IsDetached(this)) { BurnerGrate.For(this).Say("받침을 먼저 올려 놓으세요"); return; }
            if (hand.PlaceAtAnchor(transform))
            {
                // Body, not pivot, over the flame (a pan's pivot is at its rim by
                // the handle) - its base's middle, right on the grate's center.
                Vector3 body = vessel.BodyBottomWorld();
                vessel.transform.position += new Vector3(transform.position.x - body.x, transform.position.y - body.y + vesselLift, 0f);
                CurrentVessel = vessel;
                vessel.CurrentContainer = this;
                vessel.SetBurner(this);
            }
            return;
        }

        // Empty hand: the flame is only ever lit with the knob (StoveKnob), never
        // by clicking the burner itself. A pot sitting here is what the player
        // meant to grab - its collider can sit behind the burner's.
        if (hand?.Current == null && CurrentVessel != null) CurrentVessel.ForceActivate();
    }

    public void Release(Carryable item)
    {
        if ((object)CurrentVessel == item)
        {
            CurrentVessel.SetBurner(null);
            CurrentVessel = null;
        }
    }

    /// <summary>Sets on/off directly rather than toggling - for a control (the
    /// physical knob) that always knows the absolute state it wants, unlike a
    /// click which only ever flips whatever the current state happens to be.</summary>
    public void SetOn(bool on)
    {
        if (on && !IsOn) RecordLit();
        IsOn = on;
        UpdateVisual();
    }

    [Header("이 화구를 켜면 기록 (최종 평가 - 조리 중엔 표시 안 함)")]
    [Tooltip("Left empty: lighting it isn't recorded. Its rule (points, 감점/실격) is in ExamScoring: record.(category).(this).")]
    [SerializeField] private string litRecord = "";
    [SerializeField] private string litRecordCategory = ExamDeductions.Process;

    /// <summary>Burners lit this session whose lighting is recorded (DishEvaluation's 실격 check reads it).</summary>
    public static readonly System.Collections.Generic.HashSet<string> RecordedLit = new System.Collections.Generic.HashSet<string>();

    /// <summary>이 기록이 어느 화구의 '켬' 기록인지 (최종 평가에서 실격 항목과 겹치지 않게).</summary>
    public static bool IsLitRecord(string category, string reason)
    {
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None))
            if (!string.IsNullOrEmpty(b.litRecord) && b.litRecord == reason && b.litRecordCategory == category) return true;
        return false;
    }

    private void RecordLit()
    {
        if (string.IsNullOrEmpty(litRecord)) return;
        RecordedLit.Add(name);
        ExamDeductions.Add(litRecordCategory, litRecord); // (once - however often it's lit)
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => RecordedLit.Clear();

    /// <summary>Switches how high the flame is set - independent of on/off, same
    /// as a real stove knob's position stays put whether the burner is lit or
    /// not. Public so the physical lever/knob prop (added separately) can call
    /// this directly instead of going through Activate()'s click-to-toggle.</summary>
    public void SetLevel(FlameLevel level)
    {
        Level = level;
        if (IsOn) UpdateVisual();
    }

    private void UpdateVisual()
    {
        SetFlameActive(flameWeak, IsOn && Level == FlameLevel.Weak);
        SetFlameActive(flameMedium, IsOn && Level == FlameLevel.Medium);
        SetFlameActive(flameStrong, IsOn && Level == FlameLevel.Strong);
    }

    private void SetFlameActive(GameObject flame, bool active)
    {
        if (flame == null) return;
        flame.SetActive(active);

        // Some VFX prefabs don't have Play On Awake set (they're meant to be
        // triggered programmatically) - SetActive alone leaves them sitting
        // idle with zero particles instead of actually starting the effect.
        if (active)
        {
            foreach (var ps in flame.GetComponentsInChildren<ParticleSystem>(true)) ps.Play();
        }
    }
}
