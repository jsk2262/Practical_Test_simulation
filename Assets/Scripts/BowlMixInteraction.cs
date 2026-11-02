using System.Collections.Generic;
using UnityEngine;

/// <summary>집을 내용물 종류.</summary>
public enum BowlPickupKind
{
    BatterPiece, // 기름 온도 확인용 작은 반죽 조각
    MeatPiece    // 고기 한 점
}

/// <summary>
/// 그릇 하나의 버무리기 횟수 제한과 '집을 것' 선택 (그릇마다 따로 - 서로 섞이지 않음).
/// 처음 쓸 때 그 그릇(MovableBowl)에 저절로 붙는다.
///
/// - 단계: 밑간(고기에 밑간만) → 튀김옷(앙금녹말·흰자 등 튀김옷이 들어가는 순간
///   전환, 이때만 횟수 0으로). 그릇을 들거나 선택창을 다시 열어도 초기화하지 않는다.
/// - 밑간은 5회, 튀김옷은 10회까지만 버무린다 (손 버무리기 MeatKneader는 한 번의
///   주무르기 동작이 끝까지 끝났을 때 1회, 숟가락 등 도구로 젓기는 한 번 저을 때 1회).
///   중간에 멈춘 동작은 세지 않는다.
/// - 다 버무린 뒤 그 그릇(이나 고기)을 빈손으로 누르면 더 버무리지 않고, 계란 사용과
///   같은 선택창(EggPartChooser)으로 [반죽 조각] [고기 한 점] [닫기].
///   밑간 단계에선 반죽 조각을 고를 수 없다(회색). 남은 게 없으면 그 버튼도 회색.
/// - 고기 한 점: 기존 한 점 떼어내기(IngredientItem.SplitOffPiece) - 밑간·튀김옷·무게·
///   조리 상태가 그 조각과 함께 옮겨진다(복제가 아니라 이동). 마지막 한 점이면 그대로 든다.
/// - 반죽 조각: 고기의 튀김옷에서 batterPieceMl만큼 실제로 덜어낸 작은 반죽(FoodScrap
///   "반죽조각") - 기름에 넣으면 기름 온도 확인(MovableBowl.DropBatterTest), 음식물
///   쓰레기로도 버릴 수 있다.
/// - 손이 차 있거나 집기에 실패하면 그릇 내용물은 그대로.
/// - 그릇을 들거나, 고기가 다 빠지거나, 화면이 바뀌면 선택창을 닫는다.
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class BowlMixInteraction : MonoBehaviour
{
    public enum MixStage { Seasoning, Batter }

    public const string BatterPieceKind = "반죽조각";

    [Header("횟수 제한")]
    [SerializeField, Min(1)] private int seasoningLimit = 5;
    [SerializeField, Min(1)] private int batterLimit = 10;
    [Tooltip("반죽 조각 하나에 덜어내는 튀김옷 양 (mL).")]
    [SerializeField] private float batterPieceMl = 2.5f;

    [Header("현재 상태 (확인용)")]
    [SerializeField] private MixStage stage = MixStage.Seasoning;
    [SerializeField, Min(0)] private int completedMixCount;

    private MovableBowl bowl;
    private bool chooserOpen;
    private bool repeatMeatPickup; // 이 그릇에서 고기를 고른 뒤에는 한 점씩 바로 집기

    public bool RepeatsMeatPickup => repeatMeatPickup;
    public MixStage Stage => stage;
    public int CompletedMixCount => completedMixCount;
    public int MixLimit => stage == MixStage.Seasoning ? seasoningLimit : batterLimit;
    public bool IsMixingFinished => completedMixCount >= MixLimit;

    void Awake() => bowl = GetComponent<MovableBowl>();

    public static BowlMixInteraction For(MovableBowl bowl)
    {
        if (bowl == null) return null;
        BowlMixInteraction m = bowl.GetComponent<BowlMixInteraction>();
        if (m == null) m = bowl.gameObject.AddComponent<BowlMixInteraction>();
        m.SyncStage();
        return m;
    }

    // ---- 그릇 속 고기 ----

    private List<IngredientItem> MeatIn()
    {
        var list = new List<IngredientItem>();
        if (bowl == null) return list;
        foreach (IngredientItem m in bowl.GetComponentsInChildren<IngredientItem>())
            if (m.Container == bowl && m.Category == IngredientCategory.Meat) list.Add(m);
        return list;
    }

    private BatterCoating BatterIn()
    {
        foreach (IngredientItem m in MeatIn())
            if (BatterCoating.Of(m, false) is BatterCoating b && b.HasAny) return b;
        return null;
    }

    /// <summary>버무릴 게 있는 고기가 있는지 (밑간이나 튀김옷이 묻은).</summary>
    private bool HasMixableMeat()
    {
        foreach (IngredientItem m in MeatIn())
            if (m.HasMarinade || (BatterCoating.Of(m, false) is BatterCoating b && b.HasAny)) return true;
        return false;
    }

    /// <summary>튀김옷이 들어가는 순간 튀김옷 단계로 - 이때만 횟수를 0으로.</summary>
    private void SyncStage()
    {
        if (stage == MixStage.Seasoning && BatterIn() != null)
        {
            stage = MixStage.Batter;
            repeatMeatPickup = false;
            completedMixCount = 0;
            Debug.Log($"[버무리기] {bowl.name}: 튀김옷이 들어와 튀김옷 버무리기 단계로 ({batterLimit}회까지)");
        }
    }

    // ---- 횟수 ----

    /// <summary>지금 버무리기를 시작할 수 있는지 (버무릴 고기가 있고, 이 단계 횟수가 남음).</summary>
    public bool CanMix()
    {
        SyncStage();
        return HasMixableMeat() && !IsMixingFinished;
    }

    /// <summary>다 버무려서 누르면 선택창을 열 때인지.</summary>
    public bool ShowsChooser()
    {
        SyncStage();
        return HasMixableMeat() && IsMixingFinished;
    }

    /// <summary>버무리기 한 번이 끝까지 끝났을 때 (손 주무르기 한 사이클 / 도구로 한 번 젓기).
    /// 이 단계 마지막 횟수였으면 true.</summary>
    public bool CountCompletedMix()
    {
        SyncStage();
        if (IsMixingFinished) return true;
        completedMixCount = Mathf.Min(completedMixCount + 1, MixLimit);
        Debug.Log($"[버무리기] {bowl.name}: {(stage == MixStage.Seasoning ? "밑간" : "튀김옷")} {completedMixCount}/{MixLimit}회");
        if (!IsMixingFinished) return false;
        Debug.Log($"[버무리기] {bowl.name}: {(stage == MixStage.Seasoning ? "밑간" : "튀김옷")} 버무리기 끝 - 그릇을 누르면 집을 것을 고릅니다");
        return true;
    }

    // ---- 선택창 ----

    public bool HasContent(BowlPickupKind kind)
    {
        if (kind == BowlPickupKind.MeatPiece) return MeatIn().Count > 0;
        return stage == MixStage.Batter && BatterIn() is BatterCoating b && b.BatterMl >= batterPieceMl;
    }

    /// <summary>계란 사용과 같은 선택창: [반죽 조각] [고기 한 점] [닫기].</summary>
    public void OpenChooser()
    {
        if (!ShowsChooser()) return;
        if (repeatMeatPickup)
        {
            TryPickup(BowlPickupKind.MeatPiece);
            return;
        }
        Bounds seen = new Bounds(bowl.BodyCenterWorld(), Vector3.zero);
        foreach (Renderer r in bowl.GetComponentsInChildren<Renderer>())
            if (r.enabled && !(r is ParticleSystemRenderer)) seen.Encapsulate(r.bounds);
        Vector3 at = seen.center; // 버무린 그릇의 화면 중심
        var options = new[]
        {
            new EggPartChooser.Option("반죽\n조각", new Color(0.98f, 0.95f, 0.85f, 0.97f), HasContent(BowlPickupKind.BatterPiece), () => TryPickup(BowlPickupKind.BatterPiece)),
            new EggPartChooser.Option("고기\n한 점", new Color(0.95f, 0.72f, 0.66f, 0.97f), HasContent(BowlPickupKind.MeatPiece), () => TryPickup(BowlPickupKind.MeatPiece)),
            new EggPartChooser.Option("닫기", new Color(0.85f, 0.85f, 0.85f, 0.97f), true, () => { }),
        };
        EggPartChooser.ShowOptions(at, Mathf.Max(seen.extents.x, seen.extents.y), "집을 것 고르기", options);
        chooserOpen = true;
    }

    /// <summary>손이 비었을 때만, 실제로 한 점(한 조각)을 든다. 실패하면 그릇은 그대로.</summary>
    public bool TryPickup(BowlPickupKind kind)
    {
        chooserOpen = false;
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current != null || hand.IsLocked) { Debug.Log("[버무리기] 손이 비어 있지 않아 집을 수 없어요"); return false; }
        if (!HasContent(kind)) return false;

        if (kind == BowlPickupKind.MeatPiece)
        {
            IngredientItem meat = MeatIn()[0];
            IngredientItem one = null;
            if (meat.GetComponent<FoodPieceGroup>() is FoodPieceGroup g && g.Count >= 2)
            {
                // (가장 위에 있는 조각 - 손이 집기 쉬운 것)
                FoodPiece top = null;
                foreach (FoodPiece p in g.Pieces)
                    if (p != null && (top == null || p.transform.position.y > top.transform.position.y)) top = p;
                one = meat.SplitOffPiece(top);
            }
            IngredientItem take = one != null ? one : meat; // (마지막 한 점이면 그대로)
            // (손이 빈 걸 위에서 확인했으므로 실패할 일은 없다 - 혹시 실패하면 기록)
            if (!hand.PickUp(take)) { Debug.LogWarning($"[버무리기] {take.name}: 들지 못함"); return false; }
            repeatMeatPickup = true;
            Debug.Log($"[버무리기] {bowl.name}: 고기 한 점 집음 ({take.AmountGrams:0.#}g)");
            return true;
        }

        // 반죽 조각: 고기의 튀김옷에서 실제로 덜어낸다 (든 뒤에 - 못 들면 덜지 않음)
        BatterCoating batter = BatterIn();
        Color col = new Color(0.97f, 0.95f, 0.86f);
        FoodScrap blob = FoodScrap.Create("튀김옷", BatterPieceKind, col, 0.09f, bowl.BodyCenterWorld());
        if (!hand.PickUp(blob)) { Destroy(blob.gameObject); return false; }
        if (!batter.TakeMl(batterPieceMl)) { hand.Detach(); Destroy(blob.gameObject); return false; }
        Debug.Log($"[버무리기] {bowl.name}: 반죽 조각 집음 - 기름에 넣으면 온도 확인");
        return true;
    }

    /// <summary>반죽 조각인지 (기름 온도 확인용).</summary>
    public static bool IsBatterPiece(Carryable c) => c is FoodScrap s && s.Kind == BatterPieceKind;

    void Update()
    {
        // 그릇을 들거나, 고기가 다 빠지면 선택창을 닫는다
        if (chooserOpen && (!EggPartChooser.IsOpen || bowl == null || bowl.IsHeld || MeatIn().Count == 0))
        {
            if (EggPartChooser.IsOpen && (bowl == null || bowl.IsHeld || MeatIn().Count == 0)) EggPartChooser.Close();
            chooserOpen = false;
        }
    }
}
