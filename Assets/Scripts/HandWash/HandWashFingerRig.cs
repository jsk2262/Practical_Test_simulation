using UnityEngine;

public sealed class HandWashFingerRig : MonoBehaviour
{
    [Tooltip("Use _L for the uploaded Gleechi rig; use _R only for an actual right-hand rig.")]
    public string boneSuffix = "_L";
    public Vector3 curlAxis = Vector3.forward;
    private Transform[] bones;
    private Quaternion[] rest;

    public bool Bind()
    {
        if (bones != null) return true;
        var found = new Transform[15];
        var rotations = new Quaternion[15];
        string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        string[] joints = { "A", "B", "C" };
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int f = 0; f < 5; f++)
        for (int j = 0; j < 3; j++)
        {
            int n = f * 3 + j;
            string nameToFind = fingers[f] + joints[j] + boneSuffix;
            foreach (Transform bone in all)
                if (bone.name == nameToFind) { found[n] = bone; break; }
            if (found[n] == null)
            {
                Debug.LogError("Wash hand: missing bone " + nameToFind, this);
                return false;
            }
            rotations[n] = found[n].localRotation;
        }
        bones = found; rest = rotations;
        return true;
    }

    // x/y/z = proximal/middle/distal angle, not Euler xyz.
    public void Apply(Vector3 thumb, Vector3 fingers, float spread, float blend)
    {
        if (bones == null) return;
        Vector3 axis = curlAxis.sqrMagnitude > 0.001f ? curlAxis.normalized : Vector3.forward;
        for (int f = 0; f < 5; f++)
        for (int j = 0; j < 3; j++)
        {
            int n = f * 3 + j;
            Vector3 angles = f == 0 ? thumb : fingers;
            Quaternion target = rest[n] * Quaternion.AngleAxis(angles[j], axis);
            if (j == 0 && f > 0)
                target *= Quaternion.AngleAxis((f - 2.5f) * spread, Vector3.up);
            bones[n].localRotation = Quaternion.Slerp(bones[n].localRotation, target, blend);
        }
    }
    public void Restore()
    {
        if (bones == null) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null) bones[i].localRotation = rest[i];
    }
}
