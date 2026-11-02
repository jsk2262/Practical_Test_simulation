using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Controls only: no recipe answers or scoring. Never consumes mouse input.</summary>
public class ContextControlHint : MonoBehaviour
{
    private ClickDetector detector;
    private string message = "";
    private float progress = -1f;
    private float nextPoll;
    private float changedAt;
    private GUIStyle textStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<ContextControlHint>() == null)
            new GameObject("ContextControlHint").AddComponent<ContextControlHint>();
    }

    private bool Hidden()
    {
        var plan = FindFirstObjectByType<CookingPlanManager>();
        return ExamBriefing.IsPlaying || ConfirmDialog.IsOpen || ExamResultView.IsOpen
            || (plan != null && plan.IsShowing);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.1f;
        if (Hidden()) { SetHint(""); return; }
        if (detector == null) detector = FindFirstObjectByType<ClickDetector>();
        Mouse mouse = Mouse.current;
        if (mouse == null || detector == null) { SetHint(""); return; }
        progress = -1f;
        if (EggPartChooser.IsOpen) { SetHint("집을 내용물을 선택하세요 · 취소하려면 닫기"); return; }
        if (CutMethodChooser.Instance != null && CutMethodChooser.Instance.IsOpen)
        { SetHint("썰기 방법과 규격을 선택하세요"); return; }
        if (MeatKneader.InputCaptured)
        {
            ShowMix(MeatKneader.ActiveBowl, true);
            return;
        }
        if (BurnerGrate.InputCaptured) { SetHint("표시된 보관 위치나 원래 화구 클릭: 내려놓기 · Esc: 되돌리기"); return; }
        if (ClothWiper.InputCaptured) { SetHint("마우스를 누른 채 더러운 곳을 문질러 닦으세요"); return; }
        if (KitchenCamera.BlocksWorldInput) { SetHint("화면을 옮기는 중이에요"); return; }
        if (KitchenCamera.IsOverButtons(mouse.position.ReadValue()))
        { SetHint("버튼을 클릭해 화면을 확대하거나 돌아가세요"); return; }
        if (UnityEngine.EventSystems.EventSystem.current != null
            && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        { SetHint(""); return; }
        var hand = HandCarry.Instance;
        if (hand != null && hand.IsLocked) { SetHint("동작이 끝날 때까지 잠깐 기다려 주세요"); return; }
        Carryable held = hand != null ? hand.Current : null;
        GameObject target = detector.HintTarget(mouse.position.ReadValue());
        MovableBowl bowl = target != null ? target.GetComponentInParent<MovableBowl>() : null;
        IngredientItem ingredient = target != null ? target.GetComponentInParent<IngredientItem>() : null;
        if (bowl == null && ingredient != null) bowl = ingredient.Container;
        if (held is MovableBowl serving && serving.GetComponent<PlatingDestination>() != null)
        {
            var shelf = target != null ? target.GetComponentInParent<SubmitShelf>() : null;
            SetHint(shelf != null ? (shelf.CanAcceptDrop(serving) ? "클릭하면 제출대 빈 칸에 접시를 놓아요" : "제출대가 가득 찼거나 이미 제출했어요")
                : "완성 접시를 들었어요 · 오른쪽 제출대의 빈 칸을 클릭하세요");
            return;
        }
        if (held != null)
        {
            if (held is Strainer strainer)
            {
                SetHint(strainer.HasContents ? "뜰채에 건졌어요 · 옮길 접시나 그릇을 클릭하세요"
                    : "초록 테두리가 건질 범위예요 · 원하는 위치를 클릭하면 넣었다 들어 올려요");
                return;
            }
            if (BowlMixInteraction.IsBatterPiece(held))
            { SetHint(bowl != null && bowl.HasFryingOil ? "클릭하면 반죽조각을 기름에 넣어요" : "반죽조각을 들었어요 · 기름이 든 웍을 클릭하세요"); return; }
            if (held is FryingChopsticks gripping && gripping.Pinched != null)
            { SetHint("젓가락으로 집었어요 · 옮길 접시나 그릇을 클릭하세요"); return; }
            if (held is FryingChopsticks)
            { SetHint(bowl != null && bowl.HasFryingOil ? "누른 채 움직이기: 젓기 · 고기 클릭: 집기" : "튀김젓가락을 들었어요 · 기름 속 고기를 가리켜 보세요"); return; }
            if (held.GetComponent<EggCracker>() is EggCracker egg && bowl != null && egg.CanCrackInto(bowl))
            { SetHint("그릇을 꾹 누르면 계란을 깨서 담아요"); return; }
            if (held is ToolItem tool && tool.Kind == ToolKind.MeasuringSpoon)
            { SetHint("클릭: 뜨기·넣기 · 꾹 누르기: 큰술/작은술 전환"); return; }
            if (held is ToolItem knife && knife.Kind == ToolKind.Knife)
            { SetHint("재료를 클릭해 작업하세요 · 빈 조리대를 꾹 누르면 칼을 놓아요"); return; }
            if (held is IngredientItem && bowl != null)
            { SetHint("그릇을 클릭하면 들고 있는 재료를 넣어요"); return; }
            SetHint("물건을 들었어요 · 놓을 곳을 가리켜 보세요"); return;
        }
        if (bowl != null && !bowl.IsCookingVessel && ShowMix(bowl, false)) return;
        if (target != null && target.GetComponentInParent<BurnerGratePart>() != null)
        { SetHint(CleanupMode.IsOn ? "받침을 꾹 누르면 들어요 · 불을 끄고 냄비를 먼저 내려주세요" : "화구 받침을 옮기려면 정리를 켜세요"); return; }
        if (target != null && target.GetComponentInParent<StoveKnob>() != null)
        { SetHint("클릭할 때마다 불 세기가 바뀌어요 · 끔 → 강 → 중 → 약 → 끔"); return; }
        if (bowl != null)
        {
            if (bowl.IsDish && !CleanupMode.IsOn && !bowl.WouldPickUp) // 이동이 막힌 접시는 정리 모드를 안내한다.
                SetHint("접시를 옮기려면 정리 버튼을 눌러 초록색으로 켠 뒤 접시를 클릭하세요");
            else if (bowl.IsDish && CleanupMode.IsOn && bowl.HasIngredients) // 재료가 남아 있으면 먼저 꺼내야 한다.
                SetHint("정리가 켜졌어요 · 접시의 재료를 먼저 꺼낸 뒤 접시를 클릭하세요");
            else if (bowl.IsDish) // 이동 가능한 접시는 짧게 클릭해 집는다.
                SetHint("접시를 클릭하면 집어 들어요 · 놓을 곳을 클릭하세요");
            else SetHint("그릇을 꾹 누르면 집어 들어요"); // 냄비와 일반 그릇의 조작은 유지한다.
            return;
        }
        if (target != null && target.GetComponentInParent<ToolItem>() != null)
        { SetHint("도구를 꾹 누르면 집어 들어요"); return; }
        if (ingredient != null) { SetHint("재료를 클릭하면 집거나 재료에 맞는 작업을 해요"); return; }
        SetHint(KitchenCamera.IsZoomed ? "휠 버튼을 누른 채 끌기: 화면 이동 · 노란 전체 보기: 돌아가기"
            : "재료나 도구에 마우스를 올려 보세요 · 아래 화구의 화구확대로 크게 볼 수 있어요");
    }

    private bool ShowMix(MovableBowl bowl, bool active)
    {
        if (bowl == null) { SetHint(""); return false; }
        var mix = bowl.GetComponent<BowlMixInteraction>();
        if (mix == null) return false;
        if (!active && !mix.CanMix() && !mix.ShowsChooser()) return false;
        progress = Mathf.Clamp01((float)mix.CompletedMixCount / mix.MixLimit);
        string stage = mix.Stage == BowlMixInteraction.MixStage.Seasoning ? "밑간" : "앙금 버무리기";
        SetHint(mix.IsMixingFinished ? (mix.RepeatsMeatPickup ? "그릇을 클릭하면 고기를 한 점씩 집어요" : "버무리기 완료! · 그릇을 클릭해 집을 내용물을 고르세요")
            : stage + " " + mix.CompletedMixCount + "/" + mix.MixLimit
            + (active ? " · 누르고 있으면 버무려요 · 우클릭 또는 Esc: 그만하기" : " · 그릇을 클릭해 버무리기를 시작하세요"));
        return true;
    }

    private void SetHint(string value)
    {
        if (message != value) changedAt = Time.unscaledTime;
        message = value;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(message) || Hidden()) return;
        float scale = Mathf.Max(0.8f, Screen.height / 900f);
        float width = Screen.width * 0.56f;
        // (화면 맨 아래에 붙여서 - 위로 올라와 아래 선반을 가리지 않게)
        Rect box = new Rect((Screen.width - width) * 0.5f, Screen.height - 74f * scale, width, 68f * scale);
        Color old = GUI.color;
        GUI.color = new Color(0.06f, 0.10f, 0.14f, 0.94f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        textStyle.fontSize = Mathf.RoundToInt(19f * scale);
        textStyle.normal.textColor = Color.Lerp(new Color(1f,0.85f,0.3f), Color.white,
            Mathf.Clamp01((Time.unscaledTime - changedAt) / 0.7f));
        GUI.Label(new Rect(box.x + 12f, box.y + 4f, box.width - 24f, box.height - 12f), message, textStyle);
        if (progress >= 0f)
        {
            GUI.color = new Color(0.2f,0.3f,0.35f);
            Rect bar = new Rect(box.x + 10f, box.yMax - 7f * scale, box.width - 20f, 4f * scale);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f,0.9f,0.65f);
            bar.width *= progress;
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
        }
        GUI.color = old;
    }
}
