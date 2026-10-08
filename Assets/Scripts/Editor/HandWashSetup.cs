using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Kitchen > Setup Hand Wash - builds everything HandWashSequence
/// needs in the open scene in one go (instead of the manual steps in its
/// README):
/// - HandWashSequenceRoot with HandWashSequence.
/// - WashOrigin under the faucet's water stream, at the hand's depth, scaled
///   so the sequence's real-size step offsets (hands ~0.18 long) match this
///   scene's much larger hand model.
/// - Wash-only copies of the right hand (HandCursor's model) and left hand
///   (SupportHandLeft's model), each under its own wrapper with
///   HandWashFingerRig, every script/collider/animator stripped so nothing
///   else drives their bones. Wrappers start inactive.
/// - References wired: water tap, origin, both hands, the ordinary hand's
///   renderers (hidden during the wash), and every WashBasin's handWash.
/// Re-running replaces the previous HandWashSequenceRoot. Undoable; save after.
/// </summary>
public static class HandWashSetup
{
    private const string RootName = "HandWashSequenceRoot";
    private const float SequenceHandLength = 0.18f;
    public const float FoamScale = 0.5f;

    [MenuItem("Tools/Kitchen/Setup Hand Wash")]
    private static void Setup()
    {
        HandController rightCtrl = Object.FindFirstObjectByType<HandController>(FindObjectsInactive.Include);
        SupportHandController leftCtrl = Object.FindFirstObjectByType<SupportHandController>(FindObjectsInactive.Include);
        WaterTap tap = Object.FindFirstObjectByType<WaterTap>(FindObjectsInactive.Include);
        if (rightCtrl == null || leftCtrl == null || tap == null)
        {
            Debug.LogError("[HandWashSetup] Needs a HandController, a SupportHandController and a WaterTap in the open scene.");
            return;
        }

        GleechiHandPoser rightModel = rightCtrl.GetComponentInChildren<GleechiHandPoser>(true);
        GleechiHandPoser leftModel = leftCtrl.GetComponentInChildren<GleechiHandPoser>(true);
        if (rightModel == null || leftModel == null)
        {
            Debug.LogError("[HandWashSetup] Hand models (GleechiHandPoser) not found under the hands.");
            return;
        }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Setup Hand Wash");
        HandWashSequence sequence = root.AddComponent<HandWashSequence>();

        HandWashFingerRig rightRig = MakeWashHand("WashHandRight", rightModel.gameObject, root.transform);
        HandWashFingerRig leftRig = MakeWashHand("WashHandLeft", leftModel.gameObject, root.transform);

        // Under the water stream, at the hands' own depth so they draw in front.
        var originGo = new GameObject("WashOrigin");
        Transform origin = originGo.transform;
        origin.SetParent(root.transform, false);
        Transform stream = new SerializedObject(tap).FindProperty("waterStream").objectReferenceValue is GameObject s ? s.transform : tap.transform;
        origin.position = new Vector3(stream.position.x, stream.position.y, rightCtrl.transform.position.z);
        origin.rotation = Quaternion.identity;
        float handLength = HandLength(rightRig.gameObject);
        origin.localScale = Vector3.one * Mathf.Max(handLength / SequenceHandLength, 0.01f);

        // Foam (steps 2-6) and paper towel (step 10), in the sequence's real-size
        // units under the origin, where the hands meet / dry.
        GameObject foam = MakeProp("Assets/Models/HandWash/SoapFoam.fbx", origin, new Vector3(0f, 0.01f, -0.02f));
        if (foam != null) foam.transform.localScale = Vector3.one * FoamScale; // full size hid the hand motion
        GameObject towel = MakeProp("Assets/Models/HandWash/PaperTowel.fbx", origin, new Vector3(0f, -0.07f, -0.02f));
        if (towel != null) towel.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        // Only the ordinary hand's renderers that are showing now.
        Renderer[] ordinary = System.Array.FindAll(rightCtrl.GetComponentsInChildren<Renderer>(true), r => r.enabled);

        var so = new SerializedObject(sequence);
        so.FindProperty("waterTap").objectReferenceValue = tap;
        so.FindProperty("washOrigin").objectReferenceValue = origin;
        so.FindProperty("leftHand").objectReferenceValue = leftRig;
        so.FindProperty("rightHand").objectReferenceValue = rightRig;
        so.FindProperty("soapVisual").objectReferenceValue = foam;
        so.FindProperty("towelVisual").objectReferenceValue = towel;
        SerializedProperty rends = so.FindProperty("ordinaryHandRenderers");
        rends.arraySize = ordinary.Length;
        for (int i = 0; i < ordinary.Length; i++) rends.GetArrayElementAtIndex(i).objectReferenceValue = ordinary[i];
        so.ApplyModifiedProperties();

        int basins = 0;
        foreach (WashBasin basin in Object.FindObjectsByType<WashBasin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bso = new SerializedObject(basin);
            bso.FindProperty("handWash").objectReferenceValue = sequence;
            bso.ApplyModifiedProperties();
            basins++;
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[HandWashSetup] Hand wash ready (origin scale {origin.localScale.x:0.##}, {ordinary.Length} ordinary hand renderers, {basins} sink(s)) - save the scene.", root);
    }

    /// <summary>Wrapper (moved by the sequence) + a stripped copy of the hand
    /// model at the model's current world pose.</summary>
    private static HandWashFingerRig MakeWashHand(string name, GameObject model, Transform parent)
    {
        var wrapper = new GameObject(name);
        wrapper.transform.SetParent(parent, false);
        wrapper.transform.position = model.transform.position;

        GameObject copy = Object.Instantiate(model, wrapper.transform);
        copy.name = model.name + "_Wash";
        copy.SetActive(true);
        copy.transform.SetPositionAndRotation(model.transform.position, model.transform.rotation);
        copy.transform.localScale = model.transform.lossyScale;

        // Nothing but the wash sequence may drive these bones/renderers.
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Animator a in copy.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
        foreach (Rigidbody rb in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

        HandWashFingerRig rig = wrapper.AddComponent<HandWashFingerRig>();
        rig.boneSuffix = "_L"; // both scene hands use the Gleechi rig's _L bone names
        wrapper.SetActive(false);
        return rig;
    }

    /// <summary>Blender-made prop (Assets/Models/HandWash) under the origin,
    /// collider-free and hidden until its step.</summary>
    private static GameObject MakeProp(string path, Transform origin, Vector3 localPos)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) { Debug.LogWarning($"[HandWashSetup] {path} not found - skipped."); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model, origin);
        go.transform.localPosition = localPos;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        go.layer = LayerMask.NameToLayer("Ignore Raycast");
        go.SetActive(false);
        return go;
    }

    private static float HandLength(GameObject hand)
    {
        bool wasActive = hand.activeSelf;
        hand.SetActive(true);
        Renderer[] rs = hand.GetComponentsInChildren<Renderer>(true);
        float length = 0f;
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            length = Mathf.Max(b.size.x, b.size.y);
        }
        hand.SetActive(wasActive);
        return length > 0f ? length : SequenceHandLength;
    }
}
