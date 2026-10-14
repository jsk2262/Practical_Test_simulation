using UnityEditor;
using UnityEngine;

/// <summary>Play-mode checks for the hand wash: start it as if the tap was
/// turned on and the sink clicked empty-handed, and grab the Game view.</summary>
public static class HandWashPlayTest
{
    [MenuItem("Tools/Kitchen/Test/Start Hand Wash (Play Mode)")]
    private static void Start()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[HandWashTest] Enter Play Mode first."); return; }
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>();
        if (tap != null && !tap.IsOn) ((IClickable)tap).OnClicked();
        WashBasin basin = Object.FindFirstObjectByType<WashBasin>();
        if (basin != null) ((IClickable)basin).OnClicked();
        HandWashSequence seq = Object.FindFirstObjectByType<HandWashSequence>();
        Debug.Log($"[HandWashTest] tap={(tap != null && tap.IsOn)} playing={(seq != null && seq.IsPlaying)}");
    }

    /// <summary>Step 1 (wetting) with the hands brought together, palms touching,
    /// like the rubbing steps - they used to wet apart at ±0.1.</summary>
    [MenuItem("Tools/Kitchen/Hand Wash: Hands Together In Step 1")]
    private static void HandsTogetherStep1()
    {
        HandWashSequence seq = Object.FindFirstObjectByType<HandWashSequence>(FindObjectsInactive.Include);
        if (seq == null || seq.steps == null || seq.steps.Length == 0) { Debug.LogWarning("[HandWashTest] no sequence"); return; }
        Undo.RecordObject(seq, "Hands Together In Step 1");
        HandWashSequence.Step s = seq.steps[0];
        s.leftPosition = new Vector3(-0.032f, 0, 0); s.rightPosition = new Vector3(0.032f, 0, 0);
        s.leftEuler = new Vector3(0, 75, 0); s.rightEuler = new Vector3(0, -75, 0);
        EditorUtility.SetDirty(seq);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(seq.gameObject.scene);
        Debug.Log("[HandWashTest] step 1 hands together");
    }

    [MenuItem("Tools/Kitchen/Test/Drop Below Counter")]
    private static void DropBelowCounter()
    {
        HandCarry hand = HandCarry.Instance;
        GameObject lip = GameObject.Find("Counter_FrontLip");
        Debug.Log($"[HandWashTest] lip top={(lip ? lip.GetComponent<Renderer>().bounds.max.y : float.NaN):F2}");
        foreach (string n in new[] { "bowlSize2", "Wok", "PotLid" })
        {
            Carryable c = GameObject.Find(n)?.GetComponent<Carryable>();
            if (hand == null || c == null) { Debug.Log($"[HandWashTest] {n}: none"); continue; }
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(c);
            hand.PlaceFreeAt(new Vector3(2f, -4f, 0f));
            Renderer r = c.GetComponentInChildren<Renderer>();
            Debug.Log($"[HandWashTest] {n} dropped at y=-4 -> pos={c.transform.position:F2} bottom={(r ? r.bounds.min.y : 0f):F2}");
        }
        DishShelfController shelf = GameObject.Find("PlateShelf1")?.GetComponent<DishShelfController>();
        MovableBowl plate = GameObject.Find("Plate_Model_Prepped")?.GetComponentInParent<MovableBowl>();
        Debug.Log($"[HandWashTest] cleanup={CleanupMode.IsOn} prepped plate dish={(plate != null && plate.IsDish)} shelf accepts washed plate only in cleanup");
    }

    [MenuItem("Tools/Kitchen/Test/Log Hand Wash State")]
    private static void LogState()
    {
        HandWashSequence seq = Object.FindFirstObjectByType<HandWashSequence>();
        if (seq == null) { Debug.Log("[HandWashTest] no sequence"); return; }
        Debug.Log($"[HandWashTest] playing={seq.IsPlaying} progress={seq.Progress01:0.00} washed={seq.HandsWashed} label={(seq.statusLabel != null ? seq.statusLabel.text : "-")}");
    }

    [MenuItem("Tools/Kitchen/Test/Pick Up FreePotSmall")]
    private static void PickPot() => PickUp(GameObject.Find("PotSmall") != null ? "PotSmall" : "FreePotSmall");

    [MenuItem("Tools/Kitchen/Test/Pick Up Tool_Flipper")]
    private static void PickFlipper() => PickUp("Tool_Flipper");

    private static void PickUp(string name)
    {
        HandCarry hand = HandCarry.Instance;
        GameObject go = GameObject.Find(name);
        Carryable c = go != null ? go.GetComponentInChildren<Carryable>() : null;
        if (hand == null || c == null) { Debug.LogWarning($"[HandWashTest] can't pick {name}"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        // Hand in the middle of the counter so the grip is easy to see.
        hand.transform.position = new Vector3(0f, 0.5f, hand.transform.position.z);
        Debug.Log($"[HandWashTest] picked {name}: {hand.PickUp(c)}");
    }

    [MenuItem("Tools/Kitchen/Test/Knife Chop Stroke")]
    private static void Chop()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null) return;
        if (!(hand.Current is ToolItem held && held.Kind == ToolKind.Knife))
        {
            ToolItem knife = null;
            foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
                if (t.Kind == ToolKind.Knife) knife = t;
            if (knife == null) return;
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(knife);
        }
        CuttingBoardController board = CuttingBoardController.Instance;
        if (board != null && HandController.Instance != null)
        {
            Vector3 c = board.GetComponent<Collider>().bounds.center;
            HandController.Instance.GoTo(new Vector3(c.x, c.y, HandController.Instance.transform.position.z), null);
        }
        ((ToolItem)hand.Current).PlayChopMotion();
        Debug.Log("[HandWashTest] chop stroke");
    }

    /// <summary>The 중식도 grip into the scene: hand wrapped round the handle from
    /// its right, handle out toward the right hand (knife leaning 25°).</summary>
    [MenuItem("Tools/Kitchen/Apply Cleaver Grip")]
    private static void ApplyCleaverGrip()
    {
        foreach (HandController h in Object.FindObjectsByType<HandController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(h);
            so.FindProperty("chopFingerDir").vector3Value = new Vector3(-0.9f, -0.42f, 0.6f);
            so.FindProperty("chopThumbSide").vector3Value = new Vector3(-0.42f, 0.9f, 0f);
            so.FindProperty("chopHandleGripOffset").vector3Value = new Vector3(0.12f, 0.08f, -0.3f);
            so.ApplyModifiedProperties();
        }
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.Kind != ToolKind.Knife) continue;
            var so = new SerializedObject(t);
            so.FindProperty("chopTiltZ").floatValue = 25f;
            so.ApplyModifiedProperties();
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("[HandWashTest] cleaver grip applied - save the scene");
    }

    /// <summary>Reads "fx fy fz tx ty tz gx gy gz" from Temp/chopaim.txt and
    /// applies it as the chop grip (finger dir, thumb side, grip offset).</summary>
    [MenuItem("Tools/Kitchen/Test/Apply Chop Aim From File")]
    private static void ApplyChopAim()
    {
        string[] p = System.IO.File.ReadAllText("Temp/chopaim.txt").Split(new[] { ' ', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
        HandController.Instance?.SetChopAim(new Vector3(F(0), F(1), F(2)), new Vector3(F(3), F(4), F(5)), new Vector3(F(6), F(7), F(8)));
        if (p.Length > 9)
            foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
                if (t.Kind == ToolKind.Knife) t.SetChopTilt(F(9));
        Debug.Log("[HandWashTest] chop aim " + string.Join(" ", p));
    }

    [MenuItem("Tools/Kitchen/Test/Take Kitchen Towel")]
    private static void TakeTowel()
    {
        HandCarry hand = HandCarry.Instance;
        GameObject roll = GameObject.Find("Tool_KitchenTowel");
        if (hand == null || roll == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position + Vector3.right * 0.6f);
        ((IClickable)roll.GetComponent<ToolItem>()).OnClicked();
        int sheets = 0;
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Tool_KitchenTowel_Sheet")) sheets++;
        MovableBowl pan = GameObject.Find("Pan_01a")?.GetComponent<MovableBowl>();
        Debug.Log($"[HandWashTest] roll still at {roll.transform.position:F2} active={roll.activeInHierarchy}, holding={(hand.Current != null ? hand.Current.name : "-")}, sheets={sheets}, pan oil={(pan != null && pan.HasFryingOil)}");
    }

    [MenuItem("Tools/Kitchen/Test/Blanch Peas (4x speed)")]
    private static void BlanchPeas()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = GameObject.Find("FreePotSmall")?.GetComponent<MovableBowl>();
        StoveBurner burner = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        IngredientItem pea = null;
        foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.name.Contains("완두콩") || i.name.Contains("Pea")) pea = i;
        if (hand == null || pot == null || burner == null || pea == null) { Debug.LogWarning("[HandWashTest] blanch setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);

        hand.PickUp(pot);
        burner.ForceActivate();        // pot onto the burner
        burner.SetOn(true);
        pot.SetFilledWithWater(true);
        hand.PickUp(pea);
        pot.ForceActivate();           // peas into the pot
        Time.timeScale = 4f;
        Debug.Log($"[HandWashTest] pot on burner={burner.CurrentVessel == pot} water={pot.IsFilledWithWater} pea in pot={(object)pea.CurrentContainer == pot}");
    }

    [MenuItem("Tools/Kitchen/Test/Log Peas")]
    private static void LogPeas()
    {
        foreach (CookableItem c in Object.FindObjectsByType<CookableItem>(FindObjectsSortMode.None))
            if (c.name.Contains("완두콩") || c.name.Contains("Pea"))
            {
                MovableBowl pot = GameObject.Find("FreePotSmall")?.GetComponent<MovableBowl>();
                Debug.Log($"[HandWashTest] t={Time.time:0} boiling={(pot != null && pot.IsBoiling)} pea stage={c.CurrentStage} color={c.GetComponent<Renderer>().material.color}");
            }
    }

    [MenuItem("Tools/Kitchen/Test/Click Board Wash Button")]
    private static void ClickBoardWash()
    {
        CuttingBoardWashZone zone = Object.FindFirstObjectByType<CuttingBoardWashZone>();
        CuttingBoardController board = CuttingBoardController.Instance;
        if (zone == null || board == null) return;
        Debug.Log($"[HandWashTest] board before: {board.transform.position:F2} occupied={(board.Current != null ? board.Current.name : "-")}");
        zone.OnClicked();
    }

    [MenuItem("Tools/Kitchen/Test/Log Board")]
    private static void LogBoard()
    {
        CuttingBoardController board = CuttingBoardController.Instance;
        if (board != null) Debug.Log($"[HandWashTest] board at {board.transform.position:F2} hygiene={board.Hygiene.LastCategory} locked={HandCarry.Instance?.IsLocked}");
    }

    [MenuItem("Tools/Kitchen/Test/Show Cut Chooser")]
    private static void ShowCutChooser()
    {
        CuttingBoardController board = CuttingBoardController.Instance;
        Vector3 at = board != null ? board.GetComponent<Collider>().bounds.center + Vector3.up * 0.9f : Vector3.zero;
        at.z = -2f;
        if (HandCarry.Instance?.Current is ToolItem knife && knife.Kind == ToolKind.Knife)
        {
            at = knife.GetComponentInChildren<Renderer>().bounds.center;
            at.z -= 0.6f;
        }
        CutMethodChooser.Instance?.Show(at, (m, s) => Debug.Log($"[HandWashTest] cut chosen: {m} {s.widthCm}x{s.lengthCm}x{s.thicknessCm}"));
    }

    /// <summary>The 규격 screen typed into with real key events: julienne 0.3 / 5 / 0.3
    /// (Enter between boxes), then 사각썰기 with its one box.</summary>
    [MenuItem("Tools/Kitchen/Test/Spec: Type Julienne Then Cube")]
    private static void SpecTyping()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        CutMethodChooser ch = CutMethodChooser.Instance;
        if (kb == null || ch == null) return;
        string result = "";
        ch.Show(new Vector3(0f, 0f, -2f), (m, s) => result = $"{m} {s.widthCm}x{s.lengthCm}x{s.thicknessCm}");
        var steps = new System.Collections.Generic.Queue<System.Action>();
        void Type(string text)
        {
            foreach (char c in text)
            {
                char ch2 = c;
                steps.Enqueue(() => UnityEngine.InputSystem.InputSystem.QueueTextEvent(kb, ch2));
            }
        }
        void Enter()
        {
            steps.Enqueue(() => UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Enter)));
            steps.Enqueue(() => UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState()));
        }
        steps.Enqueue(() => ch.Choose(IngredientItem.CutMethod.Julienne));
        Type("0.3"); Enter(); Type("5"); Enter(); Type("0.3"); Enter();
        steps.Enqueue(() => Debug.Log($"[HandWashTest] julienne result: {result}"));
        steps.Enqueue(() => { result = ""; ch.Show(new Vector3(0f, 0f, -2f), (m, s) => result = $"{m} {s.widthCm}x{s.lengthCm}x{s.thicknessCm}"); });
        steps.Enqueue(() => ch.Choose(IngredientItem.CutMethod.Cube));
        Type("1.5"); Enter();
        steps.Enqueue(() => Debug.Log($"[HandWashTest] cube result: {result}"));
        int f = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => { if (!Application.isPlaying || steps.Count == 0) { EditorApplication.update -= tick; return; } if (++f % 3 == 0) steps.Dequeue()(); };
        EditorApplication.update += tick;
    }

    [MenuItem("Tools/Kitchen/Test/Spec: Open Cube")]
    private static void SpecOpenCube()
    {
        CutMethodChooser ch = CutMethodChooser.Instance;
        ch?.Show(new Vector3(0f, 0f, -2f), (m, s) => { });
        ch?.Choose(IngredientItem.CutMethod.Cube);
    }

    [MenuItem("Tools/Kitchen/Test/Choose Julienne")]
    private static void ChooseJulienne() => CutMethodChooser.Instance?.Choose(IngredientItem.CutMethod.Julienne);

    private static IngredientItem FindIngredient(string key)
    {
        foreach (IngredientItem i in Object.FindObjectsByType<IngredientItem>(FindObjectsSortMode.None))
            if (i.name.Contains(key)) return i;
        return null;
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Sheet On Board + Loin")]
    private static void TowelOnBoard()
    {
        HandCarry hand = HandCarry.Instance;
        KitchenTowelDispenser box = Object.FindFirstObjectByType<KitchenTowelDispenser>();
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem loin = FindIngredient("돼지등심");
        if (hand == null || box == null || board == null || loin == null) { Debug.LogWarning("[HandWashTest] towel test setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);

        ((IClickable)box).OnClicked();
        Debug.Log($"[HandWashTest] from tissue box, holding: {(hand.Current != null ? hand.Current.name : "-")}");
        board.ForceActivate();
        Debug.Log($"[HandWashTest] board liner={board.HasTowelLiner} holding={(hand.Current != null ? hand.Current.name : "-")}");
        hand.PickUp(loin);
        board.ForceActivate();
        Debug.Log($"[HandWashTest] loin on board={(object)loin.CurrentContainer == board} needsDrain={loin.NeedsTowelDrain}");
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Log Drain + Deductions")]
    private static void LogDrain()
    {
        IngredientItem loin = FindIngredient("돼지등심");
        Debug.Log($"[HandWashTest] loin drained={(loin != null && loin.IsTowelDrained)} deductions=[{string.Join(" | ", ExamDeductions.Items)}]");
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Minced Pork Into Wok (no towel)")]
    private static void MincedIntoWok()
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem pork = FindIngredient("돼지고기");
        MovableBowl wok = null;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (b.GetComponent<WokDragHandler>() != null) wok = b;
        if (hand == null || pork == null || wok == null) { Debug.LogWarning("[HandWashTest] wok test setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(pork);
        wok.ForceActivate();
        Debug.Log($"[HandWashTest] pork in {wok.name}={(object)pork.CurrentContainer == wok}");
    }

    [MenuItem("Tools/Kitchen/Test/Hang Utensils On Racks")]
    private static void HangUtensils()
    {
        HandCarry hand = HandCarry.Instance;
        HangingRack[] racks = Object.FindObjectsByType<HangingRack>(FindObjectsSortMode.None);
        if (hand == null || racks.Length == 0) { Debug.LogWarning("[HandWashTest] no racks"); return; }
        int i = 0;
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
        {
            if (!t.IsHangable || t.IsHeld) continue;
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(t);
            racks[i++ % racks.Length].ForceActivate();
            Debug.Log($"[HandWashTest] hung {t.name}: on rack={(t.CurrentContainer is HangingRack)} pos={t.transform.position:F2}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Chopsticks: Pinch Loin -> Plate")]
    private static void ChopsticksTest()
    {
        HandCarry hand = HandCarry.Instance;
        FryingChopsticks chop = Object.FindFirstObjectByType<FryingChopsticks>();
        IngredientItem loin = FindIngredient("돼지등심");
        MovableBowl plate = GameObject.Find("PlatingPlate1")?.GetComponent<MovableBowl>();
        if (hand == null || chop == null || loin == null || plate == null) { Debug.LogWarning("[HandWashTest] chopsticks test setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);

        hand.PickUp(chop);
        ((IClickable)loin).OnClicked();
        Debug.Log($"[HandWashTest] pinched={(chop.Pinched != null ? chop.Pinched.name : "-")} holding={hand.Current?.name}");
        ((IClickable)plate).OnClicked();
        Debug.Log($"[HandWashTest] after drop: pinched={(chop.Pinched != null ? chop.Pinched.name : "-")} loin on plate={(object)loin.CurrentContainer == plate} holding={hand.Current?.name}");
    }

    [MenuItem("Tools/Kitchen/Test/Pot To Sink (held)")]
    private static void PotToSink()
    {
        HandCarry hand = HandCarry.Instance;
        GameObject potGo = GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall");
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        if (hand == null || potGo == null || sink == null) return;
        Carryable pot = potGo.GetComponent<Carryable>();
        if (hand.Current != pot)
        {
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(pot);
        }
        Vector3 s = sink.GetComponent<Collider>().bounds.center;
        HandController.Instance?.GoTo(new Vector3(s.x, s.y, HandController.Instance.transform.position.z), null);
        SupportHandController sup = Object.FindFirstObjectByType<SupportHandController>();
        Renderer pr = pot.GetComponent<Renderer>();
        GleechiHandPoser poser = Object.FindFirstObjectByType<SupportHandController>()?.GetComponentInChildren<GleechiHandPoser>(true);
        MeshFilter pmf = pot.GetComponent<MeshFilter>();
        Debug.Log($"[HandWashTest] pot pivot={pot.transform.position:F2} rot={pot.transform.eulerAngles:F0} base={pot.BaseRotation.eulerAngles:F0} bounds={pr.bounds.center:F2}/{pr.bounds.size:F2} readable={(pmf && pmf.sharedMesh ? pmf.sharedMesh.isReadable.ToString() : "-")} knuckle={(poser ? poser.KnucklePosition.ToString("F2") : "-")}");
        Debug.Log($"[HandWashTest] holding {pot.name}; support hand active={(sup != null && sup.GetComponentInChildren<GleechiHandPoser>(true).gameObject.activeInHierarchy)}");
    }

    [MenuItem("Tools/Kitchen/Test/Pot Into Sink + Burner On")]
    private static void PotIntoSinkBurnerOn()
    {
        HandCarry hand = HandCarry.Instance;
        GameObject potGo = GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall");
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        if (hand == null || potGo == null || sink == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(potGo.GetComponent<Carryable>());
        ((IClickable)sink).OnClicked();
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None)) b.SetOn(true);
        Debug.Log("[HandWashTest] pot in sink, burners on");
    }

    [MenuItem("Tools/Kitchen/Test/Cleanup Flow")]
    private static void CleanupFlow()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl plate = null;
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (b.IsDish && b.IsEmpty && !b.name.Contains("Bowl_")) { plate = b; break; }
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        DishShelfController shelf = GameObject.Find("PlateShelf1")?.GetComponent<DishShelfController>();
        CuttingBoardWashZone washBtn = Object.FindFirstObjectByType<CuttingBoardWashZone>();
        CleanupTabButton tab = Object.FindFirstObjectByType<CleanupTabButton>();
        if (hand == null || plate == null || sink == null || shelf == null || tab == null) { Debug.LogWarning("[HandWashTest] cleanup setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);

        ((IClickable)plate).OnClicked();
        Debug.Log($"[HandWashTest] cleanup OFF: clicked {plate.name} -> holding={hand.Current?.name ?? "-"}, wash button collider={washBtn?.GetComponent<Collider>().enabled}");

        tab.OnClicked();
        ((IClickable)plate).OnClicked();
        Debug.Log($"[HandWashTest] cleanup ON: holding={hand.Current?.name ?? "-"}");
        ((IClickable)sink).OnClicked();
        Debug.Log($"[HandWashTest] at sink: washed={plate.IsWashed} holding={hand.Current?.name ?? "-"}");
        ((IClickable)shelf).OnClicked();
        Debug.Log($"[HandWashTest] shelf: on PlateShelf1={(object)plate.CurrentContainer == shelf} holding={hand.Current?.name ?? "-"}");
    }

    [MenuItem("Tools/Kitchen/Test/Pick Up Strainer")]
    private static void PickStrainer()
    {
        HandCarry hand = HandCarry.Instance;
        Strainer s = Object.FindFirstObjectByType<Strainer>();
        if (hand == null || s == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.transform.position = new Vector3(0f, 0.5f, hand.transform.position.z);
        hand.PickUp(s);
        Debug.Log($"[HandWashTest] holding {s.name}");
    }

    [MenuItem("Tools/Kitchen/Test/Plan: Double-Click 3 Cards")]
    private static void DoubleClickCards()
    {
        PlanStepCard[] cards = Object.FindObjectsByType<PlanStepCard>(FindObjectsSortMode.None);
        System.Array.Sort(cards, (a, b) => b.transform.position.y != a.transform.position.y
            ? b.transform.position.y.CompareTo(a.transform.position.y) : a.transform.position.x.CompareTo(b.transform.position.x));
        CookingPlanManager plan = Object.FindFirstObjectByType<CookingPlanManager>();
        for (int i = 0; i < 3 && i < cards.Length; i++)
        {
            plan.PlaceInNextSlot(cards[i]);
            Debug.Log($"[HandWashTest] {cards[i].Label.Replace("\n", " ")} -> slot {cards[i].SlotIndex + 1}");
        }
    }

    private static bool swapBurners;

    [MenuItem("Tools/Kitchen/Test/Water + Burner Placement (swapped)")]
    private static void WaterAndBurnersSwapped() { swapBurners = true; WaterAndBurners(); swapBurners = false; }

    [MenuItem("Tools/Kitchen/Test/Water + Burner Placement")]
    private static void WaterAndBurners()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>();
        MovableBowl bowl = GameObject.Find("bowlSize2")?.GetComponent<MovableBowl>();
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        MovableBowl wok = GameObject.Find("Wok")?.GetComponent<MovableBowl>();
        MovableBowl pan = GameObject.Find("FryingPan")?.GetComponent<MovableBowl>();
        StoveBurner bl = GameObject.Find("Burner_L")?.GetComponent<StoveBurner>();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        if (hand == null || sink == null || tap == null || bowl == null || pot == null || wok == null || pan == null || bl == null || br == null)
        { Debug.LogWarning("[HandWashTest] water/burner setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);

        CleanupMode.Set(true); // (bowl may be a dish)
        hand.PickUp(bowl); sink.ForceActivate();   // bowl down in the sink
        CleanupMode.Set(false);
        if (!tap.IsOn) tap.ForceActivate();         // water on: should fill the bowl in the sink
        if (swapBurners) { hand.PickUp(wok); br.ForceActivate(); hand.PickUp(pan); bl.ForceActivate(); }
        else { hand.PickUp(wok); bl.ForceActivate(); hand.PickUp(pan); br.ForceActivate(); }
        Renderer wr = wok.GetComponent<Renderer>();
        foreach (Renderer rr in wok.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"[HandWashTest] wok part {rr.name} en={rr.enabled} layer={LayerMask.LayerToName(rr.gameObject.layer)} mats={string.Join(",", System.Array.ConvertAll(rr.sharedMaterials, m => m ? m.name + "/" + m.shader.name + "/q" + m.renderQueue : "null"))} bounds={rr.bounds.center:F2}/{rr.bounds.size:F2}");
        Camera camM = Camera.main;
        Debug.Log($"[HandWashTest] wok on screen={camM.WorldToScreenPoint(wr.bounds.center):F0} cullingMask has layer={(camM.cullingMask & (1 << wok.gameObject.layer)) != 0}");
        Debug.Log($"[HandWashTest] wok pivot={wok.transform.position:F2} rot={wok.transform.eulerAngles:F0} parent={wok.transform.parent?.name} rend={wr.enabled} active={wok.gameObject.activeInHierarchy} bounds={wr.bounds.center:F2}/{wr.bounds.size:F2} container={(wok.CurrentContainer as Object)?.name} held={wok.IsHeld}");
        Debug.Log($"[HandWashTest] wok body {wok.BodyCenterWorld():F2} vs burner L {bl.transform.position:F2}; pan body {pan.BodyCenterWorld():F2} vs burner R {br.transform.position:F2}");
    }

    [MenuItem("Tools/Kitchen/Test/Pot In Sink + Water")]
    private static void PotInSinkWater()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>();
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        if (hand == null || sink == null || tap == null || pot == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(pot); sink.ForceActivate();
        if (!tap.IsOn) tap.ForceActivate();
        Transform w = pot.transform.Find("WaterFill");
        Debug.Log($"[HandWashTest] pot body={pot.BodyCenterWorld():F2} water={(w ? w.position.ToString("F2") : "none")}");
    }

    [MenuItem("Tools/Kitchen/Test/Pick Up Bowl (left hand)")]
    private static void PickBowlLeft()
    {
        HandCarry hand = HandCarry.Instance;
        Carryable bowl = GameObject.Find("bowlSize2")?.GetComponent<Carryable>();
        if (hand == null || bowl == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        ((IClickable)bowl).OnClicked();
        SupportHandController left = SupportHandController.Instance;
        Debug.Log($"[HandWashTest] bowl held={bowl.IsHeld} left at={left?.transform.position:F2} right at={HandController.Instance?.transform.position:F2} bowl at={bowl.transform.position:F2}");
    }

    /// <summary>A plate from the back of the counter set down by the cutting
    /// board (used to sink under the tilted counter top and vanish).</summary>
    [MenuItem("Tools/Kitchen/Test/Drop Back Plate Near Board")]
    private static void DropPlateNearBoard()
    {
        HandCarry hand = HandCarry.Instance;
        Carryable plate = GameObject.Find("plateSize2")?.GetComponent<Carryable>();
        if (hand == null || plate == null) return;
        CleanupMode.Set(true);
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(plate);
        hand.PlaceFreeAt(new Vector3(-1.4f, -0.75f, 0f));
        CleanupMode.Set(false);
        Debug.Log($"[HandWashTest] plateSize2 at {plate.transform.position:F2} (counter z there {HandCarry.CounterZ(-0.75f):F2}, rest {plate.RestY:F2}/{plate.RestZ:F2})");
    }

    private static CondimentDish MustardDish()
    {
        foreach (CondimentDish d in Object.FindObjectsByType<CondimentDish>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (d.name == "Dish_Mustard") { d.gameObject.SetActive(true); return d; }
        return null;
    }

    /// <summary>겨자 개기 step 1: fill the measuring cup, spoon one spoonful of
    /// water out of it, put it on the mustard powder.</summary>
    [MenuItem("Tools/Kitchen/Test/Mustard: Spoon Water In")]
    private static void MustardWater()
    {
        HandCarry hand = HandCarry.Instance;
        ToolItem spoon = ToolItem.MeasuringSpoon;
        MovableBowl cup = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "measuringCup");
        CondimentDish mustard = MustardDish();
        if (hand == null || spoon == null || cup == null || mustard == null) { Debug.Log("[HandWashTest] mustard setup missing"); return; }
        cup.SetFilledWithWater(true);
        if (hand.Current != spoon)
        {
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(spoon);
        }
        if (spoon.IsSpoonLoaded) spoon.ConsumeSeasoning();
        ((IClickable)cup).OnClicked();
        string scooped = spoon.LoadedSeasoning;
        ((IClickable)mustard).OnClicked();
        Debug.Log($"[HandWashTest] spoon scooped '{scooped}', cup water={cup.WaterAmount:F2}, mustard={mustard.Mustard}, spoon now='{spoon.LoadedSeasoning}'");
    }

    [MenuItem("Tools/Kitchen/Test/Mustard: Stir")]
    private static void MustardStir()
    {
        CondimentDish mustard = MustardDish();
        if (mustard == null) return;
        ((IClickable)mustard).OnClicked();
        Debug.Log($"[HandWashTest] stirring, mustard={mustard.Mustard}");
    }

    [MenuItem("Tools/Kitchen/Test/Mustard: Log")]
    private static void MustardLog() => Debug.Log($"[HandWashTest] mustard={MustardDish()?.Mustard} name={MustardDish()?.SeasoningName} at={MustardDish()?.transform.position:F2}");

    /// <summary>Spoon in hand scoops from a 종지 / bottle named in Temp/scoop.txt.</summary>
    [MenuItem("Tools/Kitchen/Test/Spoon Scoop From (file)")]
    private static void SpoonScoop()
    {
        string target = System.IO.File.ReadAllText("Temp/scoop.txt").Trim();
        HandCarry hand = HandCarry.Instance;
        ToolItem spoon = ToolItem.MeasuringSpoon;
        GameObject go = null;
        foreach (Carryable cc in Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None))
            if (cc.name.Trim() == target) go = cc.gameObject;
        if (hand == null || spoon == null || go == null) { Debug.Log($"[HandWashTest] scoop setup missing ({target})"); return; }
        if (hand.Current != spoon)
        {
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(spoon);
        }
        if (spoon.IsSpoonLoaded) spoon.ConsumeSeasoning();
        ((IClickable)go.GetComponent<Carryable>()).OnClicked();
        Transform c = spoon.transform.Find("mesh_0/SpoonContent") ?? spoon.GetComponentInChildren<MeshFilter>()?.transform.Find("SpoonContent");
        Debug.Log($"[HandWashTest] spoon loaded={spoon.LoadedSeasoning} content={(c ? c.position.ToString("F2") : "none")} spoon={spoon.transform.position:F2}");
    }

    [MenuItem("Tools/Kitchen/Test/Take Sheet From Tissue Box")]
    private static void TakeSheet()
    {
        HandCarry hand = HandCarry.Instance;
        KitchenTowelDispenser box = Object.FindFirstObjectByType<KitchenTowelDispenser>();
        if (hand == null || box == null) { Debug.Log("[HandWashTest] no box"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        var so = new SerializedObject(box);
        Object template = so.FindProperty("sheetTemplate").objectReferenceValue;
        ((IClickable)box).OnClicked();
        Debug.Log($"[HandWashTest] towel box clicked: template={(template ? template.name : "NONE")} hand={hand.Current?.name} locked={hand.IsLocked}");
    }

    /// <summary>Knife carry, step 1: a carrot onto the board and cut (julienne).</summary>
    [MenuItem("Tools/Kitchen/Test/Knife Carry 1: Cut Carrot")]
    private static void KnifeCarryCut()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem carrot = FindIngredient("당근");
        if (hand == null || board == null || carrot == null) { Debug.Log("[HandWashTest] knife carry setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(carrot);
        board.ForceActivate();
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
            if (t.Kind == ToolKind.Knife && !(t is FryingChopsticks)) { hand.PickUp(t); break; }
        carrot.PerformCut(IngredientItem.CutMethod.Julienne);
        Debug.Log($"[HandWashTest] carrot on board={carrot.CurrentContainer == (object)board}, cutting with {hand.Current?.name}");
    }

    /// <summary>Tofu onto the board, knife in hand over it, 사각썰기 (the block dice sequence).</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu 1: Cube Cut On Board")]
    private static void TofuCubeCut()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem tofu = FindIngredient("Tofu");
        if (hand == null || board == null || tofu == null) { Debug.Log("[HandWashTest] tofu setup missing"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(tofu);
        board.ForceActivate();
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
            if (t.Kind == ToolKind.Knife && !(t is FryingChopsticks)) { hand.PickUp(t); break; }
        // 마파두부 규격: 1.5 cm 주사위
        HandController.Instance?.GoTo(tofu.transform.position + new Vector3(0f, -0.2f, -0.6f),
            () => tofu.PerformCut(IngredientItem.CutMethod.Cube, IngredientItem.BuildSpec(IngredientItem.CutMethod.Cube, 1.5f)));
    }

    /// <summary>The cut tofu into the small pot of water on the right burner, at 20x speed.</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu 2: Blanch (20x speed)")]
    private static void TofuBlanch()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        StoveBurner burner = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        IngredientItem tofu = FindIngredient("Tofu");
        if (hand == null || pot == null || burner == null || tofu == null) { Debug.LogWarning("[HandWashTest] tofu blanch setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(new Vector3(1.5f, 0.3f, 0f));
        hand.PickUp(pot);
        burner.ForceActivate();
        burner.SetOn(true);
        pot.SetFilledWithWater(true);
        hand.PickUp(tofu);
        pot.ForceActivate();
        Time.timeScale = 20f;
        Debug.Log($"[HandWashTest] tofu in pot={(object)tofu.CurrentContainer == pot} pos={tofu.transform.position:F2}");
    }

    [MenuItem("Tools/Kitchen/Test/Tofu 3: Log")]
    private static void TofuLogState()
    {
        IngredientItem tofu = FindIngredient("Tofu");
        if (tofu == null) return;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        CookableItem ck = tofu.GetComponent<CookableItem>();
        int pieces = 0;
        foreach (Transform c in tofu.transform) if (c.name.StartsWith("Piece_")) pieces++;
        Debug.Log($"[HandWashTest] tofu t={Time.time:0} state={tofu.CurrentState} pieces={pieces} boiling={(pot != null && pot.IsBoiling)} stage={ck?.CurrentStage} pos={tofu.transform.position:F2} drained={tofu.IsTowelDrained} in={(tofu.CurrentContainer as Component)?.name}");
    }

    /// <summary>Cut tofu into the small pot of water on the right burner, the water
    /// brought almost to the boil, at 10x speed (about 20 s real to blanched).</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu Flow B: Blanch (10x)")]
    private static void TofuFlowBlanch()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        StoveBurner burner = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        IngredientItem tofu = FindIngredient("Tofu");
        if (hand == null || pot == null || burner == null || tofu == null) { Debug.LogWarning("[TofuFlow] blanch setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(new Vector3(1.5f, 0.3f, 0f));
        hand.PickUp(pot);
        burner.ForceActivate();
        burner.SetOn(true);
        pot.SetFilledWithWater(true);
        typeof(MovableBowl).GetField("boilProgress", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(pot, 295f);
        hand.PickUp(tofu);
        pot.ForceActivate();
        Time.timeScale = 10f;
        Debug.Log($"[TofuFlow] 냄비에 투입: {(InList(pot, tofu) ? "냄비 목록에 있음" : "목록에 없음")}");
    }

    /// <summary>Blanched tofu out of the pot into the frying pan (on the left
    /// burner), with 두반장 x2 and 간장 x1 in it.</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu Flow C: To Pan + Sauce")]
    private static void TofuFlowPan()
    {
        Time.timeScale = 1f;
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        MovableBowl pan = GameObject.Find("FryingPan")?.GetComponent<MovableBowl>();
        StoveBurner burner = GameObject.Find("Burner_L")?.GetComponent<StoveBurner>();
        IngredientItem tofu = FindIngredient("Tofu");
        if (hand == null || pan == null || burner == null || tofu == null) { Debug.LogWarning("[TofuFlow] pan setup missing"); return; }
        if (hand.Current != null) hand.PlaceFreeAt(new Vector3(1.5f, 0.3f, 0f));
        hand.PickUp(pan);
        burner.ForceActivate();
        hand.PickUp(tofu);
        bool stillInPot = pot != null && InList(pot, tofu);
        pan.ForceActivate();
        pan.AddSeasoning("두반장");
        pan.AddSeasoning("두반장");
        pan.AddSeasoning("간장");
        Debug.Log($"[TofuFlow] 팬으로: 냄비 목록에 남음={stillInPot}, 팬 목록={InList(pan, tofu)}, 단계={tofu.GetComponent<CookableItem>()?.CurrentStage}, 조각={tofu.GetComponent<FoodPieceGroup>()?.Count}");
    }

    /// <summary>The spatula in hand, one stir of the pan; logs the coating spread.</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu Flow D: Stir")]
    private static void TofuFlowStir()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pan = GameObject.Find("FryingPan")?.GetComponent<MovableBowl>();
        ToolItem spatula = null;
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None)) if (t.Kind == ToolKind.Spatula) spatula = t;
        if (hand == null || pan == null || spatula == null) return;
        if (hand.Current != spatula)
        {
            if (hand.Current != null) hand.PlaceFreeAt(new Vector3(1.5f, 0.3f, 0f));
            hand.PickUp(spatula);
        }
        pan.ForceActivate();
        LogCoats("젓기 후");
    }

    /// <summary>Pan in hand, 담기 onto PlatingPlate1.</summary>
    [MenuItem("Tools/Kitchen/Test/Tofu Flow E: Plate")]
    private static void TofuFlowPlate()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pan = GameObject.Find("FryingPan")?.GetComponent<MovableBowl>();
        PlatingDestination dest = GameObject.Find("PlatingPlate1")?.GetComponent<PlatingDestination>();
        if (hand == null || pan == null || dest == null) return;
        if (hand.Current != pan)
        {
            if (hand.Current != null) hand.PlaceFreeAt(new Vector3(1.5f, 0.3f, 0f));
            hand.PickUp(pan);
        }
        FryingPanPlating plating = pan.GetComponent<FryingPanPlating>();
        Debug.Log($"[TofuFlow] 담기 가능={plating != null && plating.CanPlateOnto(dest.Plate)}");
        plating?.PourInto(dest);
    }

    [MenuItem("Tools/Kitchen/Test/Tofu Flow F: Report")]
    private static void TofuFlowReport()
    {
        IngredientItem tofu = FindIngredient("Tofu");
        if (tofu == null) return;
        MovableBowl pan = GameObject.Find("FryingPan")?.GetComponent<MovableBowl>();
        MovableBowl plate = GameObject.Find("PlatingPlate1")?.GetComponent<MovableBowl>();
        Debug.Log($"[TofuFlow] 위치={tofu.transform.parent?.name}, 팬 목록={pan != null && InList(pan, tofu)}, 접시 목록={plate != null && InList(plate, tofu)}, "
                + $"팬 소스={(pan?.GetComponent<PanSauce>()?.Amount ?? 0f):0.##}, 접시 소스={(plate?.GetComponent<PanSauce>()?.Amount ?? 0f):0.##}");
        LogCoats("현재");
    }

    private static bool InList(MovableBowl b, Carryable c)
    {
        foreach (Carryable x in b.Contents) if (x == c) return true;
        return false;
    }

    private static void LogCoats(string when)
    {
        FoodPieceGroup g = FindIngredient("Tofu")?.GetComponent<FoodPieceGroup>();
        if (g == null) return;
        float min = 1f, max = 0f, sum = 0f, cmin = float.MaxValue, cmax = 0f;
        int distinctColors = 0;
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (FoodPiece p in g.Pieces)
        {
            min = Mathf.Min(min, p.Coat); max = Mathf.Max(max, p.Coat); sum += p.Coat;
            cmin = Mathf.Min(cmin, p.Cook); cmax = Mathf.Max(cmax, p.Cook);
            var mpb = new MaterialPropertyBlock();
            p.GetComponent<Renderer>().GetPropertyBlock(mpb);
            if (seen.Add(mpb.GetColor("_BaseColor").ToString("F2"))) distinctColors++;
        }
        Debug.Log($"[TofuFlow] {when}: 조각 {g.Count}개, 소스 코팅 {min:0.00}~{max:0.00} (평균 {sum / Mathf.Max(g.Count, 1):0.00}), 서로 다른 색 {distinctColors}가지, 익힘 {cmin:0}~{cmax:0}초");
    }

    [MenuItem("Tools/Kitchen/Test/Cut Cucumber On Board")]
    private static void CutCucumber()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem cuc = FindIngredient("Cucumber");
        if (hand == null || board == null || cuc == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(cuc); board.ForceActivate();
        cuc.PerformCut(IngredientItem.CutMethod.Julienne);
        HandController.Instance?.GoTo(new Vector3(-6.5f, -5f, -1f), null);
    }

    [MenuItem("Tools/Kitchen/Test/Log Cucumber Pieces")]
    private static void LogCucumberPieces()
    {
        IngredientItem cuc = FindIngredient("Cucumber");
        if (cuc == null) return;
        foreach (Renderer r in cuc.GetComponentsInChildren<Renderer>(true))
            Debug.Log($"[HandWashTest] {r.name} enabled={r.enabled} visible={r.isVisible} bounds={r.bounds.center:F2}/{r.bounds.size:F3} mats={r.sharedMaterials.Length}");
    }

    private static float savedSize; private static Vector3 savedPos;

    /// <summary>Zooms the game camera onto the point in Temp/zoom.txt ("x y size"); run again to restore.</summary>
    [MenuItem("Tools/Kitchen/Test/Toggle Camera Zoom (file)")]
    private static void ToggleZoom()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (savedSize > 0f) { cam.orthographicSize = savedSize; cam.transform.position = savedPos; savedSize = 0f; return; }
        string[] p = System.IO.File.ReadAllText("Temp/zoom.txt").Trim().Split(' ');
        savedSize = cam.orthographicSize; savedPos = cam.transform.position;
        cam.transform.position = new Vector3(float.Parse(p[0]), float.Parse(p[1]), savedPos.z);
        cam.orthographicSize = float.Parse(p[2]);
    }

    [MenuItem("Tools/Kitchen/Test/Knife Carry 2: Scoop Onto Blade")]
    private static void KnifeCarryScoop()
    {
        IngredientItem carrot = FindIngredient("당근");
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        if (carrot == null || knife == null) return;
        ((IClickable)carrot).OnClicked();
        HandController.Instance?.GoTo(HandController.Instance.transform.position + new Vector3(-2.5f, 1.2f, 0f), null); // carried off the board
        Debug.Log($"[HandWashTest] state={carrot.CurrentState} on blade={(object)knife.KnifeLoad == carrot}");
    }

    [MenuItem("Tools/Kitchen/Test/Knife Carry 3: Tip Into bowlSize2")]
    private static void KnifeCarryTip()
    {
        IngredientItem carrot = FindIngredient("당근");
        ToolItem knife = HandCarry.Instance?.Current as ToolItem;
        MovableBowl bowl = GameObject.Find("bowlSize2")?.GetComponent<MovableBowl>();
        if (carrot == null || knife == null || bowl == null) return;
        ((IClickable)bowl).OnClicked();
        Debug.Log($"[HandWashTest] carrot in bowl={(object)carrot.CurrentContainer == bowl}, knife still held={(object)HandCarry.Instance.Current == knife}, blade empty={knife.KnifeLoad == null}");
    }

    /// <summary>Holding plateSize1 in clean-up mode, what a press at each screen
    /// point in Temp/points.txt ("x y" per line, top-left origin) would hit.</summary>
    [MenuItem("Tools/Kitchen/Test/Describe Points (holding plate)")]
    private static void DescribePoints()
    {
        ClickDetector cd = Object.FindFirstObjectByType<ClickDetector>();
        HandCarry hand = HandCarry.Instance;
        Carryable plate = GameObject.Find("plateSize1")?.GetComponent<Carryable>();
        if (cd == null || hand == null || plate == null) { Debug.Log($"[HandWashTest] points: cd={cd != null} hand={hand != null} plate={plate != null}"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        CleanupMode.Set(true);
        string[] lines = System.IO.File.ReadAllLines("Temp/points.txt");
        bool empty = lines.Length > 0 && lines[0].Trim() == "empty";
        if (empty) { if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position); }
        else if (hand.Current != plate)
        {
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(plate);
        }
        Physics.SyncTransforms();
        foreach (string line in lines)
        {
            string[] p = line.Split(' ');
            if (p.Length < 2) continue;
            Camera cam = Camera.main;
            Vector2 sp = new Vector2(float.Parse(p[0]) * cam.pixelWidth / 1920f, cam.pixelHeight - float.Parse(p[1]) * cam.pixelHeight / 1080f);
            Debug.Log($"[HandWashTest] point {line} -> {cd.DescribePress(sp)}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Press 시작하세요 (briefing)")]
    private static void PressStart()
    {
        CookingPlanManager plan = Object.FindFirstObjectByType<CookingPlanManager>();
        var m = typeof(CookingPlanManager).GetMethod("OnStartClicked", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (plan != null && m != null) m.Invoke(plan, null);
        ExamBriefing.LineStarted -= LogLine;
        ExamBriefing.LineStarted += LogLine;
        Debug.Log($"[HandWashTest] start pressed, briefing={ExamBriefing.IsPlaying}");
    }

    private static void LogLine(string line) => Debug.Log($"[HandWashTest] subtitle: {line} (t={Time.time:F1})");

    /// <summary>During the briefing: plateSize2 out of the shelf onto the counter
    /// (plain clicks, no 정리 tab), then two 탕수육 ingredients into it.</summary>
    [MenuItem("Tools/Kitchen/Test/Briefing: Sort Into Plate")]
    private static void BriefingSort()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl plate = GameObject.Find("plateSize2")?.GetComponent<MovableBowl>();
        if (hand == null || plate == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        ((IClickable)plate).OnClicked();
        bool picked = (object)hand.Current == plate;
        if (picked) hand.PlaceFreeAt(new Vector3(-3.2f, -0.3f, 0f));
        int moved = 0;
        foreach (string key in new[] { "돼지등심", "당근", "오이" })
        {
            IngredientItem ing = FindIngredient(key);
            if (ing == null) continue;
            ((IClickable)ing).OnClicked();
            if ((object)hand.Current != ing) continue;
            ((IClickable)plate).OnClicked();
            if ((object)ing.CurrentContainer == plate) moved++;
        }
        Debug.Log($"[HandWashTest] briefing={ExamBriefing.IsPlaying} plate picked={picked} at {plate.transform.position:F2}, ingredients moved in={moved}");
    }

    /// <summary>Real mouse input (Input System events): clicks each screen point in
    /// Temp/clicks.txt in turn ("x y" per line, 1920x1080 top-left; "drag x1 y1 x2 y2"
    /// for a drag), logging what's in hand after each.</summary>
    [MenuItem("Tools/Kitchen/Test/Simulate Mouse Clicks (file)")]
    private static void SimulateClicks()
    {
        var steps = new System.Collections.Generic.Queue<System.Action>();
        var mouse = UnityEngine.InputSystem.Mouse.current;
        Camera cam = Camera.main;
        if (mouse == null || cam == null) { Debug.Log("[HandWashTest] no mouse/camera"); return; }
        Vector2 Sp(string x, string y) => new Vector2(float.Parse(x) * cam.pixelWidth / 1920f, cam.pixelHeight - float.Parse(y) * cam.pixelHeight / 1080f);
        void Move(Vector2 p, bool down) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,
            new UnityEngine.InputSystem.LowLevel.MouseState { position = p, buttons = (ushort)(down ? 1 : 0) });

        foreach (string raw in System.IO.File.ReadAllLines("Temp/clicks.txt"))
        {
            string[] p = raw.Trim().Split(' ');
            if (p.Length >= 5 && p[0] == "drag")
            {
                Vector2 a = Sp(p[1], p[2]), b = Sp(p[3], p[4]);
                steps.Enqueue(() => Move(a, false));
                steps.Enqueue(() => Move(a, true));
                for (int i = 1; i <= 8; i++) { float t = i / 8f; steps.Enqueue(() => Move(Vector2.Lerp(a, b, t), true)); }
                steps.Enqueue(() => Move(b, false));
            }
            else if (p.Length >= 2)
            {
                Vector2 a = Sp(p[0], p[1]);
                bool hold = p.Length >= 3 && p[2] == "hold";
                steps.Enqueue(() => Move(a, false));
                steps.Enqueue(() => Move(a, true));
                if (hold) for (int i = 0; i < 40; i++) steps.Enqueue(() => Move(a, true));
                steps.Enqueue(() => Move(a, false));
            }
            string label = raw;
            steps.Enqueue(() => Debug.Log($"[HandWashTest] after '{label}': hand={HandCarry.Instance?.Current?.name ?? "empty"}"));
        }

        int frame = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!Application.isPlaying || steps.Count == 0) { EditorApplication.update -= tick; return; }
            if (++frame % 3 != 0) return; // a few editor ticks between events so the game sees each one
            steps.Dequeue()();
        };
        EditorApplication.update += tick;
    }

    [MenuItem("Tools/Kitchen/Test/Peas 1: Into Strainer, Dip In Boiling Pot")]
    private static void PeasDip()
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem peas = FindIngredient("Pea");
        MovableBowl bowl = (GameObject.Find("bowlSize1_") ?? GameObject.Find("bowlSize1x2"))?.GetComponent<MovableBowl>();
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        Strainer strainer = Object.FindFirstObjectByType<Strainer>();
        if (hand == null || peas == null || bowl == null || pot == null || br == null || strainer == null) { Debug.Log($"[HandWashTest] peas setup missing: peas={peas != null} bowl={bowl != null} pot={pot != null} burner={br != null} strainer={strainer != null}"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        // peas into their own bowl, the pot boiling on the right burner
        hand.PickUp(peas); bowl.ForceActivate();
        hand.PickUp(pot); br.ForceActivate(); pot.SetFilledWithWater(true);
        if (!br.IsOn) Object.FindFirstObjectByType<StoveKnob>()?.OnClicked();
        // strainer scoops the peas, then goes into the pot
        hand.PickUp(strainer);
        ((IClickable)bowl).OnClicked();
        bool loaded = strainer.HasContents;
        ((IClickable)pot).OnClicked();
        Debug.Log($"[HandWashTest] peas in strainer={loaded}; dipped: strainer parent={strainer.transform.parent?.name} hand={hand.Current?.name ?? "empty"} peas in pot={(object)peas.CurrentContainer == pot} burner on={br.IsOn}");
    }

    [MenuItem("Tools/Kitchen/Test/Peas Into Strainer (both ways)")]
    private static void PeasIntoStrainer()
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem peas = FindIngredient("Pea");
        IngredientItem garlic = FindIngredient("Garlic");
        Strainer strainer = Object.FindFirstObjectByType<Strainer>();
        if (hand == null || peas == null || strainer == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(strainer); hand.PlaceFreeAt(new Vector3(-1.5f, -0.4f, 0f)); // strainer down on the counter
        ((IClickable)peas).OnClicked();          // peas in hand
        ((IClickable)strainer).OnClicked();      // onto the strainer
        bool a = strainer.HasContents && peas.transform.IsChildOf(strainer.transform);
        // the other way: strainer in hand, click a piece
        hand.PickUp(strainer);
        bool b = false;
        if (garlic != null) { ((IClickable)garlic).OnClicked(); b = garlic.transform.IsChildOf(strainer.transform); }
        Debug.Log($"[HandWashTest] peas carried onto strainer={a}; strainer in hand scooped garlic={b}; hand={hand.Current?.name}");
    }

    [MenuItem("Tools/Kitchen/Test/Peas 2: Lift Out, Cold Water Rinse")]
    private static void PeasRinse()
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem peas = FindIngredient("Pea");
        Strainer strainer = Object.FindFirstObjectByType<Strainer>();
        MovableBowl cold = GameObject.Find("bowlSize2")?.GetComponent<MovableBowl>();
        if (hand == null || strainer == null || cold == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        cold.SetFilledWithWater(true);
        ((IClickable)strainer).OnClicked();
        bool lifted = strainer.HasContents;
        ((IClickable)cold).OnClicked();
        CookableItem ck = peas?.GetComponent<CookableItem>();
        Debug.Log($"[HandWashTest] lifted out={lifted} (hand={hand.Current?.name}); after cold water still in strainer={strainer.HasContents}, peas stage={ck?.CurrentStage}");
    }

    [MenuItem("Tools/Kitchen/Test/Starch: 3 Spoons + 3 Water")]
    private static void StarchSpoons()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl bowl = (GameObject.Find("bowlSize1x2_") ?? GameObject.Find("bowlSize1x2"))?.GetComponent<MovableBowl>();
        MovableBowl cup = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "measuringCup");
        ToolItem spoon = ToolItem.MeasuringSpoon;
        CondimentDish dish = null;
        foreach (CondimentDish d in Object.FindObjectsByType<CondimentDish>(FindObjectsSortMode.None))
            if (d.SeasoningName == "감자전분") dish = d;
        if (hand == null || bowl == null || cup == null || spoon == null || dish == null) { Debug.Log($"[HandWashTest] starch: bowl={bowl != null} cup={cup != null} spoon={spoon != null} dish={dish != null}"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(bowl); hand.PlaceFreeAt(new Vector3(-3.2f, -0.4f, 0f)); // out of the shelf onto the counter
        cup.SetFilledWithWater(true);
        hand.PickUp(spoon);
        for (int i = 0; i < 3; i++) { ((IClickable)dish).OnClicked(); ((IClickable)bowl).OnClicked(); }
        PortionedCondiment pc = bowl.GetComponent<PortionedCondiment>();
        string afterStarch = $"{pc?.StarchMl}mL starch {pc?.CurrentStage}";
        for (int i = 0; i < 3; i++) { ((IClickable)cup).OnClicked(); ((IClickable)bowl).OnClicked(); }
        string afterWater = $"{pc?.WaterMl}mL water {pc?.CurrentStage}";
        hand.PlaceFreeAt(new Vector3(-2.3f, -0.4f, 0f)); // spoon down beside it
        Debug.Log($"[HandWashTest] starch in {bowl.name}: after starch {afterStarch}; after water {afterWater}");
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Stir")]
    private static void StarchStir()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl bowl = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize1x2_");
        if (hand == null || bowl == null || ToolItem.MeasuringSpoon == null) return;
        if (hand.Current != ToolItem.MeasuringSpoon) { if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position); hand.PickUp(ToolItem.MeasuringSpoon); }
        ((IClickable)bowl).OnClicked();
        Debug.Log("[HandWashTest] stirring");
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Dish Onto Empty Bowl")]
    private static void StarchDishOntoBowl()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl bowl = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize1x2");
        CondimentDish dish = null;
        foreach (CondimentDish d in Object.FindObjectsByType<CondimentDish>(FindObjectsSortMode.None))
            if (d.SeasoningName == "감자전분") dish = d;
        if (hand == null || bowl == null || dish == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(dish);
        ((IClickable)bowl).OnClicked(); // the spoon dips into the dish (animated)
        Debug.Log($"[HandWashTest] dish onto {bowl.name}: scooping={dish.IsScooping}");
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Tip Spoon Into bowlSize1x2")]
    private static void StarchTip()
    {
        MovableBowl bowl = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize1x2");
        ToolItem spoon = ToolItem.MeasuringSpoon;
        string loaded = spoon?.LoadedSeasoning;
        ((IClickable)bowl).OnClicked();
        Debug.Log($"[HandWashTest] spoon had '{loaded}', hand={HandCarry.Instance.Current?.name}; bowl starch mL={bowl.GetComponent<PortionedCondiment>()?.StarchMl}");
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Skip 10 Min + Pour Off")]
    private static void StarchSkip()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        foreach (PortionedCondiment pc in Object.FindObjectsByType<PortionedCondiment>(FindObjectsSortMode.None))
        {
            typeof(PortionedCondiment).GetField("settleTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(pc, 599.5f);
            Debug.Log($"[HandWashTest] {pc.name}: stage={pc.CurrentStage} (skipping the 10 min wait)");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Pour Off At Sink")]
    private static void StarchPour()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        foreach (PortionedCondiment pc in Object.FindObjectsByType<PortionedCondiment>(FindObjectsSortMode.None))
        {
            Carryable bowl = pc.GetComponent<Carryable>();
            string before = pc.CurrentStage.ToString();
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(bowl); sink.ForceActivate();
            Debug.Log($"[HandWashTest] {pc.name}: {before} -> {pc.CurrentStage} (in hand={(object)hand.Current == bowl})");
            if ((object)hand.Current == bowl) hand.PlaceFreeAt(new Vector3(-3.2f, -0.4f, 0f));
        }
    }

    [MenuItem("Tools/Kitchen/Test/Starch: Log")]
    private static void StarchLog()
    {
        foreach (PortionedCondiment pc in Object.FindObjectsByType<PortionedCondiment>(FindObjectsSortMode.None))
            Debug.Log($"[HandWashTest] {pc.name}: {pc.CondimentName} stage={pc.CurrentStage}");
    }

    [MenuItem("Tools/Kitchen/Test/Tofu: Into Boiling Pot")]
    private static void TofuBoil()
    {
        HandCarry hand = HandCarry.Instance;
        IngredientItem tofu = FindIngredient("Tofu");
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        if (hand == null || tofu == null || pot == null || br == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        if (pot.CurrentBurner == null) { hand.PickUp(pot); br.ForceActivate(); }
        pot.SetFilledWithWater(true);
        if (!br.IsOn) Object.FindFirstObjectByType<StoveKnob>()?.OnClicked();
        hand.PickUp(tofu); ((IClickable)pot).OnClicked();
        Debug.Log($"[HandWashTest] tofu in pot={(object)tofu.CurrentContainer == pot}, boiling={pot.IsBoiling}");
    }

    [MenuItem("Tools/Kitchen/Test/Tofu: Log")]
    private static void TofuLog() => Debug.Log($"[HandWashTest] tofu stage={FindIngredient("Tofu")?.GetComponent<CookableItem>()?.CurrentStage}");

    [MenuItem("Tools/Kitchen/Test/Towel: Spread, Pork On It, Fold")]
    private static void TowelFold()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        KitchenTowelDispenser box = Object.FindFirstObjectByType<KitchenTowelDispenser>();
        IngredientItem pork = FindIngredient("돼지등심");
        if (hand == null || board == null || box == null || pork == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        ((IClickable)box).OnClicked();          // a sheet into the hand
        ((IClickable)board).OnClicked();        // spread on the board
        hand.PickUp(pork); board.ForceActivate();
        TowelSpread spread = Object.FindFirstObjectByType<TowelSpread>();
        if (spread != null) ((IClickable)spread).OnClicked();
        Debug.Log($"[HandWashTest] towel spread={spread != null} pork on board={(object)pork.CurrentContainer == board} drained={pork.IsTowelDrained}");
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Carry Bundle To Plate, Unfold")]
    private static void TowelCarry()
    {
        HandCarry hand = HandCarry.Instance;
        TowelSpread spread = Object.FindFirstObjectByType<TowelSpread>();
        MovableBowl plate = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "plateSize2");
        IngredientItem pork = FindIngredient("돼지등심");
        if (hand == null || spread == null || plate == null || pork == null) { Debug.Log("[HandWashTest] towel carry setup missing"); return; }
        CleanupMode.Set(true);
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(plate); hand.PlaceFreeAt(new Vector3(-3.2f, -0.2f, 0f));
        CleanupMode.Set(false);
        bool wasFolded = spread.IsFolded;
        ((IClickable)spread).OnClicked();          // a plain click: the bundle into the hand
        bool carried = (object)hand.Current == pork && spread.transform.IsChildOf(pork.transform);
        ((IClickable)plate).OnClicked();           // set down on the plate, still folded
        bool onPlate = (object)pork.CurrentContainer == plate;
        Debug.Log($"[HandWashTest] folded={wasFolded} carried as bundle={carried} on plate={onPlate} still folded={spread.IsFolded} plate liner={plate.HasTowelLiner}");
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Hold To Unfold")]
    private static void TowelUnfold()
    {
        TowelSpread spread = Object.FindFirstObjectByType<TowelSpread>();
        spread?.UnfoldAction.OnClicked();
        Debug.Log("[HandWashTest] unfolding");
    }

    /// <summary>Garlic and peas off the prepped plate onto the counter (real mouse
    /// events), then each clicked again to pick it back up.</summary>
    [MenuItem("Tools/Kitchen/Test/Garlic + Peas: Move Twice")]
    private static void GarlicPeas()
    {
        HandCarry hand = HandCarry.Instance;
        ClickDetector cd = Object.FindFirstObjectByType<ClickDetector>();
        Camera cam = Camera.main;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        Physics.SyncTransforms();
        foreach (string key in new[] { "Garlic", "Pea" })
        {
            IngredientItem ing = FindIngredient(key);
            if (ing == null) { Debug.Log($"[HandWashTest] {key}: none"); continue; }
            Renderer r = ing.GetComponentInChildren<Renderer>();
            Vector3 sp = cam.WorldToScreenPoint(r.bounds.center);
            string first = cd.DescribePress(sp);
            ((IClickable)ing).OnClicked();
            bool picked = (object)hand.Current == ing;
            hand.PlaceFreeAt(new Vector3(key == "Pea" ? -1.2f : -1.8f, -0.3f, 0f));
            Physics.SyncTransforms();
            sp = cam.WorldToScreenPoint(r.bounds.center);
            Debug.Log($"[HandWashTest] {ing.name}: press1 -> {first}; picked={picked}; now at {ing.transform.position:F2} visible={r.isVisible} rendEnabled={r.enabled} active={ing.gameObject.activeInHierarchy} counterZ={HandCarry.CounterZ(ing.transform.position.y):F2} boundsZ={r.bounds.min.z:F2}..{r.bounds.max.z:F2}; press2 -> {cd.DescribePress(sp)}");

            // then onto a plate, and off it again
            MovableBowl plate = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "PlatingPlate1");
            if (plate == null) continue;
            ((IClickable)ing).OnClicked();
            ((IClickable)plate).OnClicked();
            Physics.SyncTransforms();
            sp = cam.WorldToScreenPoint(r.bounds.center);
            Debug.Log($"[HandWashTest] {ing.name} on {plate.name}={(object)ing.CurrentContainer == plate} at {ing.transform.position:F2} z {r.bounds.min.z:F2} vs plate {plate.GetComponent<Renderer>().bounds.min.z:F2}..{plate.GetComponent<Renderer>().bounds.max.z:F2}; press3 -> {cd.DescribePress(sp)}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Plating 1: Cooked Pork In Pan, Pan In Hand")]
    private static void Plating1()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pan = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "FryingPan");
        IngredientItem pork = FindIngredient("돼지등심");
        if (hand == null || pan == null || pork == null) { Debug.Log("[HandWashTest] plating setup missing"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(pan); hand.PlaceFreeAt(new Vector3(1.5f, 0.2f, 0f)); // out of the cabinet onto the counter
        hand.PickUp(pork); ((IClickable)pan).OnClicked();
        CookableItem cook = pork.GetComponent<CookableItem>();
        if (cook != null)
            typeof(CookableItem).GetField("<CurrentStage>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(cook, CookableItem.Stage.Done);
        hand.PickUp(pan);
        FryingPanPlating plating = pan.GetComponent<FryingPanPlating>();
        Debug.Log($"[HandWashTest] pork in pan={(object)pork.CurrentContainer == pan} cook={cook?.CurrentStage} plating={plating != null} canPlate={plating?.CanPlate} plates={PlatingDestination.All.Count}");
    }

    [MenuItem("Tools/Kitchen/Test/Plating 2: Click Arrow")]
    private static void Plating2()
    {
        PlatingArrow arrow = Object.FindFirstObjectByType<PlatingArrow>();
        if (arrow == null) { Debug.Log("[HandWashTest] no arrow showing"); return; }
        Debug.Log($"[HandWashTest] arrow over {arrow.plate.name} at {arrow.transform.position:F2}");
        arrow.OnClicked();
    }

    [MenuItem("Tools/Kitchen/Test/Plating 3: Log")]
    private static void Plating3()
    {
        IngredientItem pork = FindIngredient("돼지등심");
        Debug.Log($"[HandWashTest] pork now in {(pork?.CurrentContainer as Object)?.name} at {pork?.transform.position:F2}, hand={HandCarry.Instance.Current?.name}");
    }

    [MenuItem("Tools/Kitchen/Test/Towel: Log")]
    private static void TowelLog() => Debug.Log($"[HandWashTest] pork drained={FindIngredient("돼지등심")?.IsTowelDrained}");

    [MenuItem("Tools/Kitchen/Test/Hand Out Of The Way")]
    private static void HandAway()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand != null && hand.Current != null) hand.PlaceFreeAt(new Vector3(4.5f, -0.6f, 0f));
        HandController.Instance?.GoTo(new Vector3(-6.5f, -5f, -1f), null);
    }

    /// <summary>The cucumber cut as a player would, with real mouse events:
    /// cucumber onto the board, knife picked up (held press), cucumber clicked
    /// (cut menu → 채썰기 → 자르기), then the cut cucumber clicked with the knife,
    /// then the counter clicked - logging where everything ends up.</summary>
    [MenuItem("Tools/Kitchen/Test/Repro: Cucumber Cut Flow")]
    private static void ReproCucumber()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        Camera cam = Camera.main;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem cuc = FindIngredient("Cucumber") ?? FindIngredient("오이");
        ToolItem knife = System.Array.Find(Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None), t => t.Kind == ToolKind.Knife && !(t is FryingChopsticks));
        if (mouse == null || cam == null || board == null || cuc == null || knife == null) { Debug.Log("[HandWashTest] repro setup missing"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();

        var steps = new System.Collections.Generic.Queue<(int wait, System.Action act)>();
        Vector2 Scr(Vector3 w) => cam.WorldToScreenPoint(w);
        Vector3 Center(Component c) { Renderer r = c.GetComponentInChildren<Renderer>(); return r != null ? r.bounds.center : c.transform.position; }
        void Mv(Vector2 p, bool down) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState { position = p, buttons = (ushort)(down ? 1 : 0) });
        void Click(System.Func<Vector3> at, int holdFrames = 1)
        {
            Vector2 p = default;
            steps.Enqueue((2, () => { p = Scr(at()); Mv(p, false); }));
            steps.Enqueue((2, () => Mv(p, true)));
            for (int i = 0; i < holdFrames; i++) steps.Enqueue((1, () => Mv(p, true)));
            steps.Enqueue((2, () => Mv(p, false)));
        }
        void Log(string what) => steps.Enqueue((2, () => Debug.Log($"[HandWashTest] {what}: hand={HandCarry.Instance.Current?.name ?? "empty"} board.Current={board.Current?.name} cuc={cuc.transform.position:F2} in={(cuc.CurrentContainer as Object)?.name} state={cuc.CurrentState} knife={knife.transform.position:F2} knifeIn={(knife.CurrentContainer as Object)?.name} load={knife.KnifeLoad?.name} boardPos={board.transform.position:F2}")));

        Click(() => Center(cuc)); Log("picked cucumber");
        Click(() => board.GetComponent<Collider>().bounds.center); Log("on board");
        Click(() => Center(knife), 40); Log("knife held");
        Click(() => Center(cuc)); Log("cut menu");
        steps.Enqueue((5, () => CutMethodChooser.Instance?.Choose(IngredientItem.CutMethod.Julienne)));
        steps.Enqueue((5, () => typeof(CutMethodChooser).GetMethod("ConfirmSpec", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(CutMethodChooser.Instance, null)));
        steps.Enqueue((400, () => { }));
        Log("after cut");
        Click(() => Center(cuc)); Log("clicked cut cucumber");
        Click(() => new Vector3(-1.6f, -0.5f, 3f)); Log("clicked counter");
        Click(() => Center(cuc)); Log("clicked cucumber again");

        int wait = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            if (!Application.isPlaying || steps.Count == 0) { EditorApplication.update -= tick; return; }
            if (wait-- > 0) return;
            var (w, act) = steps.Dequeue();
            act();
            wait = w;
        };
        EditorApplication.update += tick;
    }

    [MenuItem("Tools/Kitchen/Test/Pour Water At Sink (2 clicks)")]
    private static void PourWater()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        MovableBowl bowl = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize2");
        if (hand == null || sink == null || bowl == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(bowl);
        bowl.SetFilledWithWater(true);
        ((IClickable)sink).OnClicked();
        PlatingArrow arrow = System.Array.Find(Object.FindObjectsByType<PlatingArrow>(FindObjectsSortMode.None), a => a.name == "PourWaterArrow");
        Debug.Log($"[HandWashTest] first click: water={bowl.WaterAmount:F2} arrow shown={arrow != null && arrow.gameObject.activeSelf}");
        ((IClickable)sink).OnClicked();
        Debug.Log("[HandWashTest] second click: pouring");
    }

    [MenuItem("Tools/Kitchen/Test/Log bowlSize2 Water")]
    private static void LogBowl2() => Debug.Log($"[HandWashTest] bowlSize2 water={System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize2")?.WaterAmount:F2} hand={HandCarry.Instance.Current?.name}");

    /// <summary>Real mouse: carrot picked up, wheel turned 3 notches (45°), set
    /// down by a click on the board's left part - logged where it landed.</summary>
    [MenuItem("Tools/Kitchen/Test/Board: Place Where Clicked + Wheel Turn")]
    private static void BoardPlaceTurn()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        Camera cam = Camera.main;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem carrot = FindIngredient("Carrot");
        if (mouse == null || cam == null || board == null || carrot == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        Bounds bb = board.GetComponent<Collider>().bounds;
        Vector3 target = new Vector3(bb.min.x + bb.size.x * 0.25f, bb.center.y + bb.size.y * 0.1f, bb.center.z);
        var steps = new System.Collections.Generic.Queue<System.Action>();
        void Mv(Vector2 p, bool down, float scroll = 0f) => UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,
            new UnityEngine.InputSystem.LowLevel.MouseState { position = p, buttons = (ushort)(down ? 1 : 0), scroll = new Vector2(0f, scroll) });
        Vector2 c = cam.WorldToScreenPoint(carrot.GetComponent<Renderer>().bounds.center);
        Vector2 t = cam.WorldToScreenPoint(target);
        steps.Enqueue(() => Mv(c, false)); steps.Enqueue(() => Mv(c, true)); steps.Enqueue(() => Mv(c, false));
        for (int i = 0; i < 3; i++) { steps.Enqueue(() => Mv(c, false, 120f)); steps.Enqueue(() => Mv(c, false)); }
        steps.Enqueue(() => Mv(t, false)); steps.Enqueue(() => Mv(t, true)); steps.Enqueue(() => Mv(t, false));
        steps.Enqueue(() => Debug.Log($"[HandWashTest] carrot on board={(object)carrot.CurrentContainer == board} middle at {carrot.GetComponent<Renderer>().bounds.center:F2} (clicked {target:F2}) spin={carrot.UserSpin.eulerAngles:F0}"));
        int f = 0;
        EditorApplication.CallbackFunction tick = null;
        tick = () => { if (!Application.isPlaying || steps.Count == 0) { EditorApplication.update -= tick; return; } if (++f % 3 == 0) steps.Dequeue()(); };
        EditorApplication.update += tick;
    }

    private static MovableBowl OilVessel()
    {
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
            if (b.HasFryingOil) return b;
        return null;
    }

    /// <summary>Oil test: the wok on the right burner, a spoon of 앙금녹말 dropped
    /// in cold oil (sinks), then again with the oil at frying heat (pops up).</summary>
    [MenuItem("Tools/Kitchen/Test/Oil: Batter Drop Cold")]
    private static void OilCold()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl wok = OilVessel();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        if (hand == null || wok == null || br == null || ToolItem.MeasuringSpoon == null) { Debug.Log($"[HandWashTest] oil setup missing: oilVessel={wok != null}"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        if (wok.CurrentBurner == null) { hand.PickUp(wok); br.ForceActivate(); }
        hand.PickUp(ToolItem.MeasuringSpoon);
        ToolItem.MeasuringSpoon.LoadSeasoning("앙금녹말");
        ((IClickable)wok).OnClicked();
        Debug.Log($"[HandWashTest] {wok.name}: oil heat={wok.OilHeat01:F2} hot={wok.IsOilHot} - dropped batter");
    }

    [MenuItem("Tools/Kitchen/Test/Oil: Batter Drop Hot")]
    private static void OilHot()
    {
        MovableBowl wok = OilVessel();
        if (wok == null) return;
        typeof(MovableBowl).GetField("oilHeat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(wok, 430f);
        HandCarry hand = HandCarry.Instance;
        if (hand.Current != ToolItem.MeasuringSpoon) { if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position); hand.PickUp(ToolItem.MeasuringSpoon); }
        ToolItem.MeasuringSpoon?.LoadSeasoning("앙금녹말");
        ((IClickable)wok).OnClicked();
        Debug.Log($"[HandWashTest] {wok.name}: oil heat={wok.OilHeat01:F2} hot={wok.IsOilHot} - dropped batter");
    }

    [MenuItem("Tools/Kitchen/Test/Waste 1: Trim Onion (scraps to corner)")]
    private static void Waste1()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        IngredientItem onion = FindIngredient("Onion");
        ToolItem knife = System.Array.Find(Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None), t => t.Kind == ToolKind.Knife && !(t is FryingChopsticks));
        if (hand == null || board == null || onion == null || knife == null) { Debug.Log("[HandWashTest] waste setup missing"); return; }
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(onion); board.ForceActivate();
        hand.PickUp(knife);
        Debug.Log($"[HandWashTest] onion canTrim={onion.CanTrim}");
        onion.PerformTrim();
    }

    [MenuItem("Tools/Kitchen/Test/Waste 2: Checks")]
    private static void Waste2()
    {
        HandCarry hand = HandCarry.Instance;
        CuttingBoardController board = CuttingBoardController.Instance;
        FoodWasteBag bag = Object.FindFirstObjectByType<FoodWasteBag>();
        ToolItem knife = System.Array.Find(Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None), t => t.Kind == ToolKind.Knife && !(t is FryingChopsticks));
        if (hand == null || bag == null || knife == null) { Debug.Log("[HandWashTest] no bag/knife"); return; }
        int before = WasteLog.Entries.Count;
        FoodScrap[] scraps = board.GetComponentsInChildren<FoodScrap>();
        Debug.Log($"[HandWashTest] scraps on board: {scraps.Length} ({string.Join(", ", System.Array.ConvertAll(scraps, s => s.SourceName + " " + s.Kind))})");

        // (a) tool alone: no hint, nothing thrown
        if (hand.Current != knife) { if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position); hand.PickUp(knife); }
        bool toolHasFood = WasteRules.HasFoodToDump(knife);
        ((IClickable)bag).OnClicked();
        Debug.Log($"[HandWashTest] (a) bare knife: hasFood={toolHasFood}, still in hand={(object)hand.Current == knife}, knife alive={knife != null}");

        // (b) sweep scraps outside clean-up: refused; in clean-up: all onto the blade
        if (scraps.Length > 0) ((IClickable)scraps[0]).OnClicked();
        bool sweptOff = knife.HasScraps;
        CleanupMode.Set(true);
        if (scraps.Length > 0) ((IClickable)scraps[0]).OnClicked();
        bool sweptOn = knife.HasScraps;
        ((IClickable)bag).OnClicked();
        CleanupMode.Set(false);
        Debug.Log($"[HandWashTest] (b) sweep w/o 정리={sweptOff}, with 정리={sweptOn}; after bag: knife empty={!knife.HasScraps}, needs wash={knife.GetComponent<HygieneTracker>()?.NeedsWash}");

        // (c) a single food in hand
        IngredientItem carrot = FindIngredient("Carrot");
        hand.PlaceFreeAt(new Vector3(1f, -0.5f, 0f));
        if (carrot != null) { ((IClickable)carrot).OnClicked(); ((IClickable)bag).OnClicked(); }
        Debug.Log($"[HandWashTest] (c) carrot thrown: hand={hand.Current?.name ?? "empty"}");

        // (d) bowl emptied, bowl stays in hand
        MovableBowl bowl = System.Array.Find(Object.FindObjectsByType<MovableBowl>(FindObjectsSortMode.None), b => b.name == "bowlSize2");
        IngredientItem garlic = FindIngredient("Garlic"), peas = FindIngredient("Pea");
        if (bowl != null)
        {
            if (garlic != null) { hand.PickUp(garlic); bowl.ForceActivate(); }
            if (peas != null) { hand.PickUp(peas); bowl.ForceActivate(); }
            int inBowl = 0; foreach (Carryable c in bowl.Contents) inBowl++;
            hand.PickUp(bowl);
            ((IClickable)bag).OnClicked();
            int left = 0; foreach (Carryable c in bowl.Contents) left++;
            Debug.Log($"[HandWashTest] (d) bowl had {inBowl}, after bag {left}, bowl in hand={(object)hand.Current == bowl}");
        }

        // (e) the same food thrown twice
        IngredientItem ginger = FindIngredient("Ginger");
        int n0 = WasteLog.Entries.Count;
        if (ginger != null) { bag.Throw(ginger); bag.Throw(ginger); }
        Debug.Log($"[HandWashTest] (e) double throw logged {WasteLog.Entries.Count - n0} (should be 1); total new log entries {WasteLog.Entries.Count - before}");
    }

    [MenuItem("Tools/Kitchen/Test/Waste 3: Log Survivors")]
    private static void Waste3()
    {
        foreach (string k in new[] { "Carrot", "Garlic", "Pea", "Ginger", "Onion" })
            Debug.Log($"[HandWashTest] {k} still exists={FindIngredient(k) != null}");
        Debug.Log($"[HandWashTest] scraps left in scene={Object.FindObjectsByType<FoodScrap>(FindObjectsSortMode.None).Length}, bowlSize2 exists={GameObject.Find("bowlSize2") != null}, knife exists={System.Array.Exists(Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None), t => t.Kind == ToolKind.Knife)}");
    }

    [MenuItem("Tools/Kitchen/Test/Log Timer")]
    private static void LogTimer() => Debug.Log($"[HandWashTest] timer={Object.FindFirstObjectByType<CountdownTimer>()?.GetComponent<TextMesh>().text} briefing={ExamBriefing.IsPlaying} t={Time.time:F1}");

    [MenuItem("Tools/Kitchen/Test/Starch 2: Fill Water")]
    private static void StarchWater()
    {
        MovableBowl bowl = GameObject.Find("bowlSize2")?.GetComponent<MovableBowl>();
        bowl?.SetFilledWithWater(true);
        Debug.Log($"[HandWashTest] starch stage={bowl?.GetComponent<PortionedCondiment>()?.CurrentStage}");
    }

    [MenuItem("Tools/Kitchen/Test/Starch 3: Pour Off At Sink")]
    private static void StarchDrain()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl bowl = GameObject.Find("bowlSize2")?.GetComponent<MovableBowl>();
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        PortionedCondiment pc = bowl?.GetComponent<PortionedCondiment>();
        if (hand == null || bowl == null || sink == null || pc == null) return;
        string before = pc.CurrentStage.ToString();
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(bowl);
        sink.ForceActivate();
        Debug.Log($"[HandWashTest] starch {before} -> {pc.CurrentStage}, water={bowl.WaterAmount:F2}, in hand={(object)hand.Current == bowl}");
        if ((object)hand.Current == bowl) hand.PlaceFreeAt(new Vector3(-1.8f, -0.6f, 0f));
    }

    [MenuItem("Tools/Kitchen/Test/Drop Plates Beside Board")]
    private static void DropPlatesBesideBoard()
    {
        HandCarry hand = HandCarry.Instance;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        CleanupMode.Set(true);
        Camera cam = Camera.main;
        string[] names = { "plateSize1", "plateSize2", "bowlSize2" };
        float[] xs = { 660f, 740f, 820f };
        for (int i = 0; i < names.Length; i++)
        {
            Carryable c = GameObject.Find(names[i])?.GetComponent<Carryable>();
            if (hand == null || c == null) continue;
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(c);
            Vector3 w = cam.ScreenToWorldPoint(new Vector3(xs[i] * cam.pixelWidth / 1920f, cam.pixelHeight - 650f * cam.pixelHeight / 1080f, 10f));
            hand.PlaceFreeAt(w);
            Renderer r = c.GetComponentInChildren<Renderer>();
            Debug.Log($"[HandWashTest] {names[i]} dropped at {w:F2} -> {c.transform.position:F2} visible={r?.isVisible} active={c.gameObject.activeInHierarchy}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Describe Presses")]
    private static void DescribePresses()
    {
        ClickDetector cd = Object.FindFirstObjectByType<ClickDetector>();
        Camera cam = Camera.main;
        if (cd == null || cam == null) return;
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        Physics.SyncTransforms();
        var targets = new System.Collections.Generic.List<GameObject>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("TissueBox") || t.name.StartsWith("Ingredient_") || t.name == "measuringCup") targets.Add(t.gameObject);
        foreach (GameObject go in targets)
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r == null || !r.enabled || !go.activeInHierarchy) continue;
            Vector3 sp = cam.WorldToScreenPoint(r.bounds.center);
            Debug.Log($"[HandWashTest] press {go.name} -> {cd.DescribePress(sp)}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Cleanup: Wash Bowl + Plate To Shelves")]
    private static void CleanupBowlPlate()
    {
        HandCarry hand = HandCarry.Instance;
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        CleanupMode.Set(true);
        string[] items = { "bowlSize2", "plateSize1" };
        string[] shelves = { "PlateShelf1", "PlateShelf2" };
        for (int i = 0; i < 2; i++)
        {
            MovableBowl b = GameObject.Find(items[i])?.GetComponent<MovableBowl>();
            DishShelfController s = GameObject.Find(shelves[i])?.GetComponent<DishShelfController>();
            if (hand == null || sink == null || b == null || s == null) continue;
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(b);
            sink.ForceActivate();
            bool washed = b.IsWashed;
            s.ForceActivate();
            Debug.Log($"[HandWashTest] {items[i]}: washed={washed} on {shelves[i]}={(object)b.CurrentContainer == s}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Pot Filled On Burner")]
    private static void PotFilledOnBurner()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        if (hand == null || pot == null || br == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(pot);
        br.ForceActivate();
        pot.SetFilledWithWater(true);
        Debug.Log($"[HandWashTest] pot on burner, water={pot.WaterAmount:F2}");
    }

    [MenuItem("Tools/Kitchen/Test/Click Stove Knob")]
    private static void ClickKnob()
    {
        StoveKnob k = Object.FindFirstObjectByType<StoveKnob>();
        if (k == null) return;
        k.OnClicked();
        HandController.Instance?.GoTo(k.transform.position + new Vector3(2f, 1.5f, -0.55f), null); // out of the way for screenshots
        StoveBurner b = new SerializedObject(k).FindProperty("burner").objectReferenceValue as StoveBurner;
        Debug.Log($"[HandWashTest] knob clicked: burner {b?.name} on={b?.IsOn} level={b?.Level} knob rot={k.transform.eulerAngles:F0} pos={k.transform.position:F2}");
    }

    [MenuItem("Tools/Kitchen/Test/Pot: Sink -> Burner")]
    private static void PotSinkToBurner()
    {
        HandCarry hand = HandCarry.Instance;
        MovableBowl pot = (GameObject.Find("PotSmall") ?? GameObject.Find("FreePotSmall"))?.GetComponent<MovableBowl>();
        WashBasin sink = Object.FindFirstObjectByType<WashBasin>();
        StoveBurner br = GameObject.Find("Burner_R")?.GetComponent<StoveBurner>();
        if (hand == null || pot == null || sink == null || br == null) return;
        if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        hand.PickUp(pot);
        ((IClickable)sink).OnClicked();
        Debug.Log($"[HandWashTest] pot at sink: container={(pot.CurrentContainer as Object)?.name} parent={pot.transform.parent?.name} pos={pot.transform.position:F2}");
        ((IClickable)pot).OnClicked();
        Debug.Log($"[HandWashTest] picked again: holding={hand.Current?.name ?? "-"}");
        ((IClickable)br).OnClicked();
        Debug.Log($"[HandWashTest] on burner: container={(pot.CurrentContainer as Object)?.name} body={pot.BodyCenterWorld():F2} burner={br.transform.position:F2} holding={hand.Current?.name ?? "-"}");
    }

    [MenuItem("Tools/Kitchen/Test/Pans To Shelves")]
    private static void PansToShelves()
    {
        HandCarry hand = HandCarry.Instance;
        string[] items = { "PotSmall", "FryingPan", "Wok" };
        string[] shelves = { "KitchenShelf1", "KitchenShelf2", "KitchenShelf3" };
        for (int i = 0; i < 3; i++)
        {
            Carryable c = GameObject.Find(items[i])?.GetComponent<Carryable>();
            KitchenShelfController s = GameObject.Find(shelves[i])?.GetComponent<KitchenShelfController>();
            if (hand == null || c == null || s == null) continue;
            if (hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
            hand.PickUp(c);
            s.ForceActivate();
            Debug.Log($"[HandWashTest] {items[i]} -> {shelves[i]}: on shelf={(object)c.CurrentContainer == s} scale={c.transform.lossyScale:F2}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Bowl Water")]
    private static void LogBowlWater()
    {
        foreach (string n in new[] { "bowlSize2", "PotSmall" })
        {
            MovableBowl b = GameObject.Find(n)?.GetComponent<MovableBowl>();
            if (b != null) Debug.Log($"[HandWashTest] {n} water={b.WaterAmount:F2} pos={b.transform.position:F2}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Log Hung Utensils")]
    private static void LogHung()
    {
        foreach (ToolItem t in Object.FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
        {
            if (!t.IsHangable) continue;
            Renderer r = t.GetComponent<Renderer>();
            Debug.Log($"[HandWashTest] {t.name} container={(t.CurrentContainer is Object o ? o.name : "-")} parent={t.transform.parent?.name} rot={t.transform.eulerAngles:F0} base={t.BaseRotation.eulerAngles:F0} bounds={(r ? $"{r.bounds.center:F2}/{r.bounds.size:F2}" : "-")}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Toggle Tap")]
    private static void ToggleTap()
    {
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>();
        if (tap == null) return;
        tap.ForceActivate();
        Debug.Log($"[HandWashTest] tap on={tap.IsOn}");
    }

    [MenuItem("Tools/Kitchen/Test/Click Burners Empty-Handed")]
    private static void ClickBurners()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand != null && hand.Current != null) hand.PlaceFreeAt(hand.Current.transform.position);
        foreach (StoveBurner b in Object.FindObjectsByType<StoveBurner>(FindObjectsSortMode.None))
        {
            ((IClickable)b).OnClicked();
            Debug.Log($"[HandWashTest] clicked {b.name}: IsOn={b.IsOn}");
        }
    }

    [MenuItem("Tools/Kitchen/Test/Capture Game View (keep UI)")]
    private static void CaptureWithUi()
    {
        ScreenCapture.CaptureScreenshot("Temp/GameView.png");
        Debug.Log("[HandWashTest] captured Temp/GameView.png");
    }

    [MenuItem("Tools/Kitchen/Test/Capture Game View")]
    private static void Capture()
    {
        Object.FindFirstObjectByType<CookingPlanManager>()?.Hide();
        ScreenCapture.CaptureScreenshot("Temp/GameView.png");
        Debug.Log("[HandWashTest] captured Temp/GameView.png");
    }
}
