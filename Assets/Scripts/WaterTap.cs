using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaterTap : Interactable
{
    [SerializeField] private GameObject waterStream;

    public bool IsOn { get; private set; }

    [Header("Filling")]
    /// <summary>Where the water lands - vessels whose center is within
    /// fillRadius of this point (on screen, i.e. world XY) fill up while the tap
    /// runs. Defaults to the stream object's position.</summary>
    [SerializeField] private Transform fillPoint;
    [SerializeField] private float fillRadius = 1.2f;
    /// <summary>How far below the fill point the falling water still reaches (down into the sink).</summary>
    [SerializeField] private float fallReach = 4f;

    private ParticleSystem[] streamParticles;
    private readonly List<Coroutine> loopRoutines = new List<Coroutine>();
    private readonly List<MovableBowl> waterVessels = new List<MovableBowl>();

    void Start()
    {
        foreach (MovableBowl vessel in FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (vessel.CanHoldWater) waterVessels.Add(vessel);
    }

    void Update()
    {
        if (!IsOn) return;

        Transform point = fillPoint != null ? fillPoint : (waterStream != null ? waterStream.transform : transform);
        Vector2 spout = point.position;

        // Water falls straight down from the stream: anything under it - held
        // under the tap or sitting down in the sink - fills, not just what's
        // within fillRadius of the stream's middle (the sink bottom is well
        // below that, so a bowl set in the sink used to stay dry).
        foreach (MovableBowl vessel in waterVessels)
        {
            if (vessel == null || !vessel.isActiveAndEnabled || vessel.IsFilledWithWater) continue;
            if (CleanupMode.IsOn && vessel.IsPlateOrBowl) continue; // 정리 때 씻는 그릇에는 물이 차지 않게
            Vector2 v = vessel.BodyCenterWorld();
            if (Mathf.Abs(v.x - spout.x) > fillRadius) continue;
            if (v.y > spout.y + fillRadius || v.y < spout.y - fallReach) continue;
            vessel.AddWater(Time.deltaTime);
        }
    }

    /// <summary>Where the water comes down (world).</summary>
    public Vector3 StreamPoint => (fillPoint != null ? fillPoint : (waterStream != null ? waterStream.transform : transform)).position;

    /// <summary>Runs the water (or stops it) without the tap-click's other
    /// handling - for washing something held under it at the sink.</summary>
    public void SetRunning(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        ShowStream();
    }

    private void ShowStream()
    {
        if (waterStream == null) return;
        waterStream.SetActive(IsOn);
        if (IsOn)
        {
            if (streamParticles == null) streamParticles = waterStream.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in streamParticles) loopRoutines.Add(StartCoroutine(ReplayWhileOn(ps)));
        }
        else
        {
            foreach (var r in loopRoutines) StopCoroutine(r);
            loopRoutines.Clear();
        }
    }

    protected override void Activate()
    {
        IsOn = !IsOn;
        if (waterStream == null) return;

        ShowStream();

        if (IsOn)
        {
            // A water-holding vessel held while turning the tap on just stays in
            // hand under the stream - Update fills it (and anything set down
            // under the faucet) gradually while the water runs.
            if (HandCarry.Instance != null && HandCarry.Instance.Current is MovableBowl bowl)
            {
                if (!bowl.CanHoldWater)
                {
                    // A flat plate (or anything else that isn't a real deep
                    // bowl) can't actually be filled - SetFilledWithWater
                    // would just silently no-op, leaving it stuck in hand
                    // forever since this was the only thing that ever ran on
                    // a tap click. Set it down here instead, same as the sink
                    // itself already does for a bowl that's just resting.
                    HandCarry.Instance.PlaceAtAnchor(transform);
                    bowl.CurrentContainer = null;
                }
            }
            else if (HandCarry.Instance != null && HandCarry.Instance.Current != null)
            {
                // Anything else carried (a tool, an ingredient, a towel...) -
                // the tap sits right above the sink and is an easy misclick,
                // so it gets the same "just set it down" escape hatch rather
                // than trapping whatever's in hand until the player realizes
                // they need to aim slightly lower at the basin instead.
                Carryable item = HandCarry.Instance.Current;
                if (HandCarry.Instance.PlaceAtAnchor(transform)) item.CurrentContainer = null;
            }
        }
    }

    /// <summary>The chosen water VFX (a splash/impact prefab) is a one-shot burst,
    /// not a looping stream, so each sub-effect (the falling water, the ground
    /// ripple, etc.) needs to be re-triggered on its own to read as continuously
    /// flowing. Looping every sub-system together on the SLOWEST one's duration
    /// left the faster ones (the actual visible splash) sitting idle for a
    /// couple seconds between bursts - each now loops on its own timing instead,
    /// so the main stream never has a gap.</summary>
    private IEnumerator ReplayWhileOn(ParticleSystem ps)
    {
        while (true)
        {
            ps.Play();
            yield return new WaitForSeconds(Mathf.Max(ps.main.duration - 0.1f, 0.1f));
        }
    }
}
