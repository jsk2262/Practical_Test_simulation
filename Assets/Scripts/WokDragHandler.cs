using UnityEngine;

/// <summary>
/// Lets the player shake the wok by holding and dragging the mouse over it.
/// Exposes ShakeSpeed (screen px/sec, decaying when not actively dragged) for
/// CookableItem to read - fast dragging = stir-frying, staying still = burning.
/// </summary>
public class WokDragHandler : MonoBehaviour, IDraggable
{
    [SerializeField] private float wobbleStrength = 0.15f;
    [SerializeField] private float springBack = 12f;
    [SerializeField] private float decaySpeed = 4f;

    private Vector3 homeLocalPos;
    private StoveBurner homeBurner;
    private Vector3 currentOffset;
    private Carryable carryable;
    private MovableBowl vessel;

    public float ShakeSpeed { get; private set; }

    void Awake()
    {
        homeLocalPos = transform.localPosition;
        carryable = GetComponent<Carryable>();
        vessel = GetComponent<MovableBowl>();
    }

    void Update()
    {
        // This wobble/spring-back only makes sense while the wok is actually
        // resting on a lit burner being shaken - the same CurrentBurner gate
        // Carryable.OnDragStart already uses to decide "shake in place" vs.
        // "pick up and carry". Gating on IsHeld alone (an earlier attempt)
        // wasn't enough: once set back down anywhere else - the rack, a
        // shelf - this kept firing and forced transform.localPosition back to
        // homeLocalPos (a stale Awake-time snapshot, meaningless once
        // reparented under a new container) every single frame, silently
        // overriding whatever actually placed it there.
        bool onBurner = vessel != null && vessel.CurrentBurner != null;
        if (!onBurner)
        {
            homeBurner = null;
            return;
        }

        // Rest spot = where it was just set down on this burner. The Awake-time
        // localPosition was in the shelf's space; reused under the burner it
        // threw the wok far off every frame (it "kept going somewhere else").
        if (homeBurner != vessel.CurrentBurner)
        {
            homeBurner = vessel.CurrentBurner;
            homeLocalPos = transform.localPosition;
            currentOffset = Vector3.zero;
        }

        UpdateButtonShake();

        currentOffset = Vector3.Lerp(currentOffset, Vector3.zero, Time.deltaTime * springBack);
        transform.localPosition = homeLocalPos + currentOffset;

        ShakeSpeed = Mathf.Lerp(ShakeSpeed, 0f, Time.deltaTime * decaySpeed);

        // 우클릭 흔들기는 왼손이 손잡이를 잡고 흔든다 - 오른손(주걱 · 소스 그릇)은 흔들리지 않게
        HandController.Instance?.SetHandShake(buttonShaking ? 0f : ShakeSpeed, 300f);
    }

    // ---- 우클릭(또는 휠 버튼)으로 흔들기 ----
    // 불 위의 팬·웍을 오른쪽 버튼(확대 중이 아니면 휠 버튼도)으로 누른 채 끌면, 왼손이
    // 손잡이를 잡고 흔든다. 왼쪽 버튼은 집기·젓기에 그대로 (왼쪽 끌기 흔들기도 유지).
    // 칼을 든 채 우클릭은 왼손 재료 옮기기(LeftHandCarry)가 먼저.
    private bool buttonShaking;
    private Vector2 lastShakeMouse;

    private void UpdateButtonShake()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;
        bool middleFree = !KitchenCamera.IsZoomed; // (확대 중 휠 버튼은 화면 이동)
        bool pressedNow = mouse.rightButton.wasPressedThisFrame || (middleFree && mouse.middleButton.wasPressedThisFrame);
        bool held = mouse.rightButton.isPressed || (middleFree && mouse.middleButton.isPressed);
        Vector2 screen = mouse.position.ReadValue();

        if (!buttonShaking && pressedNow && CanButtonShake() && PointerOnThis(screen))
        {
            buttonShaking = true;
            lastShakeMouse = screen;
            OnDragStart();
        }
        if (!buttonShaking) return;
        if (!held || vessel == null || vessel.CurrentBurner == null || vessel.IsHeld)
        {
            buttonShaking = false;
            OnDragEnd();
            return;
        }
        OnDragMove(screen - lastShakeMouse, Mathf.Max(Time.deltaTime, 1e-4f));
        lastShakeMouse = screen;
    }

    private static bool CanButtonShake()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand != null && (hand.IsLocked || hand.IsDragging)) return false;
        if (hand != null && hand.Current is ToolItem t && t.Kind == ToolKind.Knife) return false; // (왼손 재료 옮기기가 먼저)
        if (PauseMenu.IsPaused || LeftHandCarry.IsCarrying || MeatKneader.InputCaptured || ConfirmDialog.IsOpen || ExamResultView.IsOpen) return false; // (일시정지 중 포함)
        return !(UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());
    }

    /// <summary>커서가 이 팬(이나 그 안의 음식, 이 팬이 올라간 화구) 위인지.</summary>
    private bool PointerOnThis(Vector2 screen)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        foreach (RaycastHit h in Physics.RaycastAll(cam.ScreenPointToRay(screen), 200f))
        {
            if (h.collider.transform.IsChildOf(transform)) return true;
            if (h.collider.GetComponentInParent<StoveBurner>() is StoveBurner b && (object)b.CurrentVessel == vessel) return true;
            if (h.collider.GetComponentInParent<IngredientItem>() is IngredientItem ing && (object)ing.Container == vessel) return true;
        }
        return false;
    }

    public bool OnDragStart()
    {
        // 흔드는 동안 왼손이 손잡이를 잡고 팬을 따라 움직인다 (손잡이로 드는 팬·웍)
        if (carryable != null && carryable.HoldsByHandle) carryable.ShowHandleHeldInPlace();
        return true;
    }

    public void OnDragMove(Vector2 mouseDelta, float deltaTime)
    {
        if (deltaTime <= 0f) return;

        vessel.GetComponent<PanSauce>()?.Agitate(mouseDelta);
        ShakeSpeed = mouseDelta.magnitude / deltaTime;

        Vector3 worldDelta = new Vector3(mouseDelta.x, mouseDelta.y, 0f) * 0.002f * wobbleStrength;
        currentOffset = Vector3.ClampMagnitude(currentOffset + worldDelta, 0.35f);

        // 팬 안의 재료가 흔드는 대로 쏠리고 튀어 오른다
        if (vessel == null) return;
        vessel.GetComponentsInChildren(tossGroups); // 재사용 리스트에 담기 (흔드는 매 프레임 새 배열 안 만듦)
        foreach (FoodPieceGroup g in tossGroups) g.Toss(mouseDelta); // 재료 묶음마다 흔든 만큼 쏠리게
        // 소스도 흔들어 섞인다 (젓는 것처럼 - 바닥에 눌어붙지 않게)
        if (ShakeSpeed > 200f && Time.time >= nextSauceMix && PanSauce.Of(vessel, false) is PanSauce sauce && sauce.Amount > 0.01f)
        {
            sauce.Stir();
            nextSauceMix = Time.time + 0.4f;
        }
    }

    private float nextSauceMix;
    private readonly System.Collections.Generic.List<FoodPieceGroup> tossGroups = new System.Collections.Generic.List<FoodPieceGroup>(); // 재료 묶음 찾기용 재사용 리스트

    public void OnDragEnd()
    {
        // 다 흔들면 왼손은 손잡이에서 놓는다 (팬을 든 게 아니라 화구 위에 그대로)
        if (carryable != null && carryable.HoldsByHandle && !carryable.IsHeld) SupportHandController.Instance?.Hide();
    }
}
