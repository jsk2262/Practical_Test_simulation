using UnityEngine;

/// <summary>How a seasoning looks once scooped: a dry heap (가루), a thin glossy
/// pool (액체), or a thick glossy dollop (점성 있는 액체).</summary>
public enum CondimentForm { Powder, Liquid, Paste }

/// <summary>
/// Every seasoning in the exam set, by its Korean name: its form and color,
/// shared by the shelf bottles, the 종지 dishes and the measuring spoon's
/// scooped dose.
/// Shelf bottles: 소금, 설탕, 백후추, 흑후추 / 간장, 식초, 청주, 참기름, 식용유.
/// 종지 dishes: 감자전분, 고춧가루, 겨자 / 두반장, 춘장, 굴소스, 케첩.
/// </summary>
public static class Condiments
{
    public struct Info
    {
        public CondimentForm form;
        public Color color;
        public Info(CondimentForm form, Color color) { this.form = form; this.color = color; }
    }

    /// <summary>A spoonful of plain water (from the measuring cup) - for 겨자 개기.</summary>
    public const string Water = "물";

    public static Info Get(string name)
    {
        switch (Base(name))
        {
            case "소금": return new Info(CondimentForm.Powder, new Color(0.97f, 0.97f, 0.97f));
            case "설탕": return new Info(CondimentForm.Powder, new Color(0.99f, 0.98f, 0.94f));
            case "백후추": return new Info(CondimentForm.Powder, new Color(0.86f, 0.82f, 0.72f));
            case "흑후추": return new Info(CondimentForm.Powder, new Color(0.22f, 0.2f, 0.18f));
            case "감자전분": return new Info(CondimentForm.Powder, new Color(0.98f, 0.98f, 0.96f));
            case "고춧가루": return new Info(CondimentForm.Powder, new Color(0.75f, 0.12f, 0.06f));
            case "겨자": return new Info(CondimentForm.Powder, new Color(0.93f, 0.8f, 0.3f));
            case Water: return new Info(CondimentForm.Liquid, new Color(0.7f, 0.85f, 0.95f));
            case "간장": return new Info(CondimentForm.Liquid, new Color(0.18f, 0.08f, 0.04f));
            case "식초": return new Info(CondimentForm.Liquid, new Color(0.95f, 0.9f, 0.75f));
            case "청주": return new Info(CondimentForm.Liquid, new Color(0.93f, 0.93f, 0.88f));
            case "참기름": return new Info(CondimentForm.Liquid, new Color(0.72f, 0.45f, 0.12f));
            case "식용유": return new Info(CondimentForm.Liquid, new Color(0.95f, 0.82f, 0.35f));
            case "고추기름": return new Info(CondimentForm.Liquid, new Color(0.8f, 0.2f, 0.05f)); // (strained through the cheesecloth)
            case "탄 고추기름": return new Info(CondimentForm.Liquid, new Color(0.3f, 0.1f, 0.04f)); // (fried too long)
            case "전분(전분물)": return new Info(CondimentForm.Powder, new Color(0.97f, 0.97f, 0.95f)); // (전분물의 전분 - 물은 따로 '물')
            case "물기 많은 앙금": return new Info(CondimentForm.Liquid, new Color(0.9f, 0.92f, 0.93f)); // (윗물 안 버린 앙금)
            // 달걀, a spoonful at a time (EggContents.SpoonName)
            case "흰자": return new Info(CondimentForm.Liquid, new Color(0.9f, 0.89f, 0.74f));
            case "푼 흰자": return new Info(CondimentForm.Liquid, new Color(0.93f, 0.92f, 0.82f));
            case "노른자": return new Info(CondimentForm.Paste, new Color(1f, 0.66f, 0.1f));
            case "푼 노른자": return new Info(CondimentForm.Liquid, new Color(1f, 0.7f, 0.15f));
            case "전란": return new Info(CondimentForm.Liquid, new Color(0.98f, 0.8f, 0.35f));
            case "계란물": return new Info(CondimentForm.Liquid, new Color(1f, 0.8f, 0.3f));
            case "두반장": return new Info(CondimentForm.Paste, new Color(0.55f, 0.12f, 0.06f));
            case "춘장": return new Info(CondimentForm.Paste, new Color(0.12f, 0.08f, 0.05f));
            case "굴소스": return new Info(CondimentForm.Paste, new Color(0.3f, 0.17f, 0.09f));
            case "앙금녹말": return new Info(CondimentForm.Paste, new Color(0.97f, 0.97f, 0.95f));
            case "케첩": return new Info(CondimentForm.Paste, new Color(0.78f, 0.1f, 0.06f));
        }
        // 갠 겨자 / 발효 겨자: the powder mixed with water is a paste.
        if (name != null && name.Contains("겨자")) return new Info(CondimentForm.Paste, new Color(0.9f, 0.75f, 0.2f));
        return new Info(CondimentForm.Powder, new Color(0.9f, 0.9f, 0.85f));
    }

    private static string Base(string name) => name?.Trim();

    /// <summary>A content mesh (a powder heap, a liquid pool, a paste dollop)
    /// as a child of parent, its "up" along upLocal, sized to fit a round
    /// container of the given radius - the inside of a 종지, or a spoon's bowl.</summary>
    public static GameObject BuildContent(Transform parent, Vector3 localCenter, Vector3 upLocal, float radius, CondimentForm form, Color color, string name = "Content")
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, upLocal.normalized);

        float smooth = form == CondimentForm.Powder ? 0.05f : form == CondimentForm.Liquid ? 0.95f : 0.75f;
        Material mat = MakeMaterial(color, smooth);

        // A heap stands up off the surface; a liquid lies flat; a paste is a
        // low glossy dome.
        if (form == CondimentForm.Liquid)
        {
            AddShape(go.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(radius * 2f, radius * 0.03f, radius * 2f), mat);
        }
        else if (form == CondimentForm.Paste)
        {
            AddShape(go.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(radius * 1.9f, radius * 0.05f, radius * 1.9f), mat);
            AddShape(go.transform, PrimitiveType.Sphere, new Vector3(0f, radius * 0.08f, 0f), new Vector3(radius * 1.4f, radius * 0.45f, radius * 1.4f), mat);
        }
        else
        {
            AddShape(go.transform, PrimitiveType.Sphere, new Vector3(0f, radius * 0.1f, 0f), new Vector3(radius * 1.8f, radius * 0.75f, radius * 1.8f), mat);
        }
        return go;
    }

    private static void AddShape(Transform parent, PrimitiveType type, Vector3 local, Vector3 scale, Material mat)
    {
        GameObject s = GameObject.CreatePrimitive(type);
        s.transform.SetParent(parent, false);
        s.transform.localPosition = local;
        s.transform.localScale = scale;
        Object.DestroyImmediate(s.GetComponent<Collider>());
        Renderer r = s.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Material MakeMaterial(Color color, float smoothness)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }
}
