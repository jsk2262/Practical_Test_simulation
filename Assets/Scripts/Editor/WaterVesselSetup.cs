using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the vessels that take water from the faucet
/// (FreePotSmall, bowlSize1, bowlSize1_, measuringCup) in the open scene:
/// turns canHoldWater on for each, and gives measuringCup - placed in the
/// scene as a bare model - the BoxCollider + MovableBowl it needs to be picked
/// up and filled. Undoable; save the scene afterwards.
/// </summary>
public static class WaterVesselSetup
{
    private static readonly string[] VesselNames = { "FreePotSmall", "bowlSize1", "bowlSize1_", "measuringCup" };

    [MenuItem("Tools/Kitchen/Setup Water Vessels")]
    private static void Setup()
    {
        int done = 0;
        foreach (string vesselName in VesselNames)
        {
            GameObject go = FindInOpenScene(vesselName);
            if (go == null)
            {
                Debug.LogWarning($"[WaterVesselSetup] '{vesselName}' not found in the open scene.");
                continue;
            }

            // MovableBowl reads the Renderer on its own GameObject - for a model
            // whose mesh lives on a single child, that child is the vessel.
            GameObject target = go;
            if (go.GetComponent<MovableBowl>() == null && go.GetComponent<Renderer>() == null)
            {
                Renderer[] rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length != 1)
                {
                    Debug.LogWarning($"[WaterVesselSetup] '{vesselName}' has {rends.Length} child renderers - add MovableBowl by hand to the mesh object.");
                    continue;
                }
                target = rends[0].gameObject;
            }

            MovableBowl bowl = target.GetComponent<MovableBowl>();
            if (bowl == null)
            {
                if (target.GetComponent<Collider>() == null) Undo.AddComponent<BoxCollider>(target);
                bowl = Undo.AddComponent<MovableBowl>(target);
            }

            var so = new SerializedObject(bowl);
            so.FindProperty("canHoldWater").boolValue = true;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(target.scene);
            Debug.Log($"[WaterVesselSetup] '{target.name}' can hold water.", target);
            done++;
        }

        Debug.Log($"[WaterVesselSetup] {done}/{VesselNames.Length} vessels set up - save the scene.");
    }

    /// <summary>Makes the scene's "measuringSpoon" model a pickup-able
    /// ToolItem of kind MeasuringSpoon - the spoon seasoning bottles summon
    /// for their one-spoonful scoop.</summary>
    [MenuItem("Tools/Kitchen/Setup Measuring Spoon")]
    private static void SetupMeasuringSpoon()
    {
        GameObject go = FindInOpenScene("measuringSpoon");
        if (go == null)
        {
            Debug.LogWarning("[WaterVesselSetup] 'measuringSpoon' not found in the open scene.");
            return;
        }

        GameObject target = go;
        if (go.GetComponent<ToolItem>() == null && go.GetComponent<Renderer>() == null)
        {
            Renderer[] rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length != 1)
            {
                Debug.LogWarning($"[WaterVesselSetup] 'measuringSpoon' has {rends.Length} child renderers - add ToolItem by hand to the mesh object.");
                return;
            }
            target = rends[0].gameObject;
        }

        ToolItem tool = target.GetComponent<ToolItem>();
        if (tool == null)
        {
            if (target.GetComponent<Collider>() == null) Undo.AddComponent<BoxCollider>(target);
            tool = Undo.AddComponent<ToolItem>(target);
        }

        var so = new SerializedObject(tool);
        so.FindProperty("kind").enumValueIndex = (int)ToolKind.MeasuringSpoon;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(target.scene);
        Debug.Log($"[WaterVesselSetup] '{target.name}' is now the measuring spoon - save the scene.", target);
    }

    private static readonly string[] HandledPanNames = { "Pan_03a", "Pan_01a", "FreePotSmall" };

    /// <summary>Turns on Carryable.holdByHandle for the long-handled pans:
    /// carried handle-down (6 o'clock), gripped by the left hand.</summary>
    [MenuItem("Tools/Kitchen/Setup Pan Handle Grip")]
    private static void SetupPanHandleGrip()
    {
        foreach (string panName in HandledPanNames)
        {
            GameObject go = FindInOpenScene(panName);
            Carryable carryable = go != null ? go.GetComponentInChildren<Carryable>(true) : null;
            if (carryable == null)
            {
                Debug.LogWarning($"[WaterVesselSetup] '{panName}' (with a Carryable) not found in the open scene.");
                continue;
            }

            var so = new SerializedObject(carryable);
            so.FindProperty("holdByHandle").boolValue = true;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(carryable.gameObject.scene);
            Debug.Log($"[WaterVesselSetup] '{carryable.name}' is carried by its handle.", carryable);
        }
    }

    private static readonly string[] PlateShelfNames = { "PlateShelf1", "PlateShelf2" };

    /// <summary>Makes PlateShelf1/PlateShelf2 dish racks (DishShelfController):
    /// washed, empty plates/bowls can be set down on them.</summary>
    [MenuItem("Tools/Kitchen/Setup Plate Shelves")]
    private static void SetupPlateShelves()
    {
        foreach (string shelfName in PlateShelfNames)
        {
            GameObject go = FindInOpenScene(shelfName);
            if (go == null)
            {
                Debug.LogWarning($"[WaterVesselSetup] '{shelfName}' not found in the open scene.");
                continue;
            }

            if (go.GetComponent<DishShelfController>() == null)
            {
                if (go.GetComponent<Collider>() == null) Undo.AddComponent<BoxCollider>(go);
                Undo.AddComponent<DishShelfController>(go);
            }

            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[WaterVesselSetup] '{shelfName}' takes washed dishes - save the scene.", go);
        }
    }

    private static GameObject FindInOpenScene(string objectName)
    {
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName) return t.gameObject;
            }
        }
        return null;
    }
}
