using System;
using UnityEngine;
using UnityEngine.Events;

// Use dedicated wash-only hand model copies, not the live carrying rig.
public sealed class HandWashSequence : MonoBehaviour
{
    [Serializable]
    public class Step
    {
        public string label;
        [Min(0.1f)] public float seconds;
        public Vector3 leftPosition, rightPosition;
        public Vector3 leftEuler, rightEuler;
        public Vector3 thumbAngles, fingerAngles;
        public float fingerSpread;
        public Vector3 rubAxis = Vector3.up;
        public float rubDistance = 0.025f;
        public Vector3 turnAxis = Vector3.forward;
        public float turnDegrees;
        public float cyclesPerSecond = 2f;
        public bool requiresWater;
        public bool showSoap;
        public bool showTowel;
    }

    public WaterTap waterTap;
    public Transform washOrigin;
    public HandWashFingerRig leftHand, rightHand;
    [Tooltip("Assign only ordinary hand renderers; enabled states are restored exactly.")]
    public Renderer[] ordinaryHandRenderers;
    public TextMesh statusLabel;
    public GameObject soapVisual;
    public GameObject towelVisual;
    public UnityEvent onCompleted;
    public UnityEvent onCancelled;
    [Min(0.01f)] public float transitionSeconds = 0.2f;
    public Step[] steps = {
        new Step { label="1 · 손과 손목 적시기", seconds=2f, requiresWater=true,
            leftPosition=new Vector3(-0.032f,0,0), rightPosition=new Vector3(0.032f,0,0),
            leftEuler=new Vector3(0,75,0), rightEuler=new Vector3(0,-75,0),
            thumbAngles=new Vector3(10,10,5), fingerAngles=new Vector3(8,10,5), turnDegrees=12, cyclesPerSecond=1 },
        new Step { label="2 · 비누 바르기", seconds=1.5f, showSoap=true,
            leftPosition=new Vector3(-0.04f,0,0), rightPosition=new Vector3(0.04f,0.02f,0),
            leftEuler=new Vector3(0,75,0), rightEuler=new Vector3(0,-75,15),
            thumbAngles=new Vector3(15,15,10), fingerAngles=new Vector3(15,20,10), turnDegrees=10 },
        new Step { label="3 · 손바닥 마주 비비기", seconds=3f, showSoap=true,
            leftPosition=new Vector3(-0.035f,0,0), rightPosition=new Vector3(0.035f,0,0),
            leftEuler=new Vector3(0,75,0), rightEuler=new Vector3(0,-75,0),
            thumbAngles=new Vector3(20,15,10), fingerAngles=new Vector3(8,12,8) },
        new Step { label="5 · 깍지 끼고 손바닥 비비기", seconds=2.5f, showSoap=true,
            leftPosition=new Vector3(-0.025f,0.015f,0), rightPosition=new Vector3(0.025f,-0.015f,0),
            leftEuler=new Vector3(0,80,-8), rightEuler=new Vector3(0,-80,8),
            thumbAngles=new Vector3(25,25,15), fingerAngles=new Vector3(25,35,20), fingerSpread=8, rubDistance=0.012f },
        new Step { label="6 · 두 손 맞물려 돌리기", seconds=2f, showSoap=true,
            leftPosition=new Vector3(-0.035f,0,0), rightPosition=new Vector3(0.035f,0,0),
            leftEuler=new Vector3(0,75,20), rightEuler=new Vector3(0,-75,-20),
            thumbAngles=new Vector3(30,30,20), fingerAngles=new Vector3(45,75,50), rubDistance=0.008f, turnDegrees=18, cyclesPerSecond=1.5f },
        new Step { label="9 · 비눗기 헹구기", seconds=2.5f, requiresWater=true,
            leftPosition=new Vector3(-0.075f,0,0), rightPosition=new Vector3(0.075f,0,0),
            leftEuler=new Vector3(0,20,-15), rightEuler=new Vector3(0,-20,15),
            thumbAngles=new Vector3(10,10,5), fingerAngles=new Vector3(10,15,8), turnDegrees=20, cyclesPerSecond=1.2f },
        new Step { label="10 · 종이수건으로 물기 닦기", seconds=1.5f, showTowel=true,
            leftPosition=new Vector3(-0.04f,-0.07f,0), rightPosition=new Vector3(0.04f,-0.07f,0),
            leftEuler=new Vector3(0,70,0), rightEuler=new Vector3(0,-70,0),
            thumbAngles=new Vector3(25,30,15), fingerAngles=new Vector3(25,40,20), rubDistance=0.02f, cyclesPerSecond=2 }
    };

    public bool IsPlaying { get; private set; }

    /// <summary>The full 1→2→3→5→6→9→10 wash (through drying with the paper
    /// towel) has been done at least once - the exam's opening hand wash.</summary>
    public bool HandsWashed { get; private set; }
    public float Progress01 => total > 0 ? Mathf.Clamp01(elapsed / total) : 0;
    private float elapsed, total, stepElapsed;
    private int stepIndex;
    private bool[] rendererStates;
    private bool leftActive, rightActive, soapActive, towelActive;
    private Vector3 leftRestPosition, rightRestPosition;
    private Quaternion leftRestRotation, rightRestRotation;
    private string oldLabel;

    private bool HandsEmpty => HandCarry.Instance == null || HandCarry.Instance.Current == null;
    private bool WaterAvailable => waterTap != null && waterTap.IsOn;

    /// <summary>지금 하는 게 간단 손씻기(1단계 적시기만)인지.</summary>
    public bool IsShortWash { get; private set; }

    /// <summary>firstStepOnly: 처음·마지막 정리 때가 아닌 중간 손씻기 - 1단계(손과
    /// 손목 적시기)만 하고 끝난다.</summary>
    public void BeginWash(bool firstStepOnly = false)
    {
        // Starts even with the tap off - step 1 (wetting) just waits, with a
        // prompt, until the water is turned on.
        if (IsPlaying || !HandsEmpty) return;
        if (washOrigin == null || leftHand == null || rightHand == null || leftHand == rightHand ||
            steps == null || steps.Length != 7) { Debug.LogError("Assign origin, two separate wash hands, and seven steps.", this); return; }
        total = 0;
        foreach (Step step in steps)
        {
            if (step == null || step.seconds <= 0) return;
            total += step.seconds;
        }
        IsShortWash = firstStepOnly;
        if (firstStepOnly) total = steps[0].seconds; // (적시기 단계에서 그대로 끝남)
        if (!leftHand.Bind() || !rightHand.Bind()) return;
        soapActive=soapVisual != null && soapVisual.activeSelf;
        towelActive=towelVisual != null && towelVisual.activeSelf;
        leftActive=leftHand.gameObject.activeSelf; rightActive=rightHand.gameObject.activeSelf;
        leftRestPosition=leftHand.transform.localPosition; rightRestPosition=rightHand.transform.localPosition;
        leftRestRotation=leftHand.transform.localRotation; rightRestRotation=rightHand.transform.localRotation;
        rendererStates=new bool[ordinaryHandRenderers == null ? 0 : ordinaryHandRenderers.Length];
        for (int i=0;i<rendererStates.Length;i++)
            if (ordinaryHandRenderers[i] != null) { rendererStates[i]=ordinaryHandRenderers[i].enabled; ordinaryHandRenderers[i].enabled=false; }
        if (statusLabel != null) oldLabel=statusLabel.text;
        leftHand.gameObject.SetActive(true); rightHand.gameObject.SetActive(true);
        elapsed=stepElapsed=0; stepIndex=0; IsPlaying=true;
        Pose(1f);
    }

    private void LateUpdate()
    {
        if (!IsPlaying) return;
        if (!HandsEmpty) { End(false); return; }
        if (steps[stepIndex].requiresWater && !WaterAvailable)
        {
            if (statusLabel != null) statusLabel.text="물을 켜면 손 씻기를 계속합니다";
            return; // no progress while required water is off
        }
        float dt=Time.deltaTime;
        elapsed+=dt; stepElapsed+=dt;
        if (elapsed >= total) { End(true); return; }
        while (stepIndex < steps.Length-1 && stepElapsed >= steps[stepIndex].seconds)
        { stepElapsed-=steps[stepIndex].seconds; stepIndex++; }
        Pose(1f-Mathf.Exp(-dt/Mathf.Max(transitionSeconds,0.01f)));
        if (statusLabel != null) statusLabel.text=steps[stepIndex].label+"  "+Mathf.CeilToInt(total-elapsed)+"초";
    }

    private void Pose(float blend)
    {
        Step s=steps[stepIndex];
        float wave=Mathf.Sin(stepElapsed*s.cyclesPerSecond*Mathf.PI*2);
        if (soapVisual != null) soapVisual.SetActive(s.showSoap);
        if (towelVisual != null) towelVisual.SetActive(s.showTowel);
        Vector3 rub=s.rubAxis.normalized*(wave*s.rubDistance);
        Quaternion turn=Quaternion.AngleAxis(wave*s.turnDegrees,s.turnAxis.sqrMagnitude>0.001f?s.turnAxis.normalized:Vector3.forward);
        Move(leftHand.transform,s.leftPosition+rub,Quaternion.Euler(s.leftEuler)*turn,blend);
        Move(rightHand.transform,s.rightPosition-rub,Quaternion.Euler(s.rightEuler)*Quaternion.Inverse(turn),blend);
        leftHand.Apply(s.thumbAngles,s.fingerAngles,s.fingerSpread,blend);
        rightHand.Apply(s.thumbAngles,s.fingerAngles,s.fingerSpread,blend);
    }
    private void Move(Transform hand,Vector3 position,Quaternion rotation,float blend)
    {
        hand.position=Vector3.Lerp(hand.position,washOrigin.TransformPoint(position),blend);
        hand.rotation=Quaternion.Slerp(hand.rotation,washOrigin.rotation*rotation,blend);
    }
    public void CancelWash() { if (IsPlaying) End(false); }
    private void End(bool completed)
    {
        IsPlaying=false;
        leftHand.Restore(); rightHand.Restore();
        leftHand.transform.SetLocalPositionAndRotation(leftRestPosition,leftRestRotation);
        rightHand.transform.SetLocalPositionAndRotation(rightRestPosition,rightRestRotation);
        leftHand.gameObject.SetActive(leftActive); rightHand.gameObject.SetActive(rightActive);
        for (int i=0;i<rendererStates.Length;i++)
            if (ordinaryHandRenderers[i] != null) ordinaryHandRenderers[i].enabled=rendererStates[i];
        if (soapVisual != null) soapVisual.SetActive(soapActive);
        if (towelVisual != null) towelVisual.SetActive(towelActive);
        if (statusLabel != null) statusLabel.text=oldLabel;
        if (completed) { if (!IsShortWash) HandsWashed = true; HandHygiene.Washed(); }
        if (completed) onCompleted?.Invoke(); else onCancelled?.Invoke();
    }
    private void OnDisable() { CancelWash(); }
}
