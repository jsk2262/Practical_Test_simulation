using System.Collections; // 기존 칼질 애니메이션과 순서대로 실행한다.
using UnityEngine; // 메시와 재질을 다룬다.

public partial class IngredientItem
{
    private IEnumerator GingerTrimRoutine() // 생강 겉면을 여섯 방향으로 깎고 위아래 껍질을 제거한다.
    {
        isCutting = true; // 작업 도중 중복 칼질을 막는다.
        var frame = new GameObject("GingerTrimFrame").transform; // 도마 기준으로 자를 좌표계를 만든다.
        frame.SetParent(transform.parent, true); // 기존 재료와 같은 부모 아래에서 작업한다.
        frame.SetPositionAndRotation(transform.position, BoardFrame()); // 도마에 수직인 방향을 위로 정한다.
        frame.localScale = Vector3.one; // 좌표계 크기를 일정하게 유지한다.
        var filter = GetComponent<MeshFilter>(); // 본체의 메시를 교체할 참조다.
        SliceMesh body = SliceMesh.From(filter, frame); // 원래 생강의 껍질과 UV를 유지한 채 작업 좌표로 옮긴다.
        Bounds bounds = body.Bounds; // 처음 크기를 기준으로 모든 절단면을 계산한다.
        Vector3 center = bounds.center; // 여섯 면의 공통 중심이다.
        Material flesh = new Material(rend.sharedMaterial) { name = "Ginger_PeeledFlesh", mainTexture = null }; // 노란 속살 전용 재질이다.
        Color yellow = new Color(1f, 0.81f, 0.29f); // 참고 이미지의 따뜻한 노란색을 사용한다.
        flesh.color = yellow; // 기본 색을 지정한다.
        if (flesh.HasProperty("_BaseColor")) flesh.SetColor("_BaseColor", yellow); // URP 색도 맞춘다.
        if (flesh.HasProperty("_BaseMap")) flesh.SetTexture("_BaseMap", null); // 껍질 텍스처가 속살에 남지 않게 한다.
        var originalMaterials = rend.sharedMaterials; // 아직 깎이지 않은 면은 원래 껍질을 유지한다.
        var materials = new Material[body.sub.Count]; // 원래 면과 새 절단면의 재질을 준비한다.
        for (int i = 0; i < materials.Length; i++) materials[i] = i < originalMaterials.Length ? originalMaterials[i] : flesh; // 마지막 서브메시는 노란 절단면이다.
        Mesh generated = null; // 직전 프레임의 임시 메시를 정리하기 위해 기록한다.
        SupportHandController.Instance?.ShowNear(transform.position); // 반대 손으로 재료를 받친다.
        try
        {
            for (int step = 0; step < 8; step++) // 옆면 여섯 번과 위아래 두 번을 깎는다.
            {
                bool side = step < 6; // 옆면과 위아래 면의 절단 방향을 구분한다.
                Quaternion rotation = side ? Quaternion.Euler(0f, -step * 60f, 0f) : Quaternion.identity; // 각 면을 기존 슬라이서의 X축에 맞춘다.
                GingerRotateMesh(body, center, rotation); // 메시 자체를 작업 좌표 안에서 회전한다.
                int axis = side ? 0 : 1; // 옆면은 X축, 위아래는 Y축으로 자른다.
                float angle = step * Mathf.PI / 3f; // 타원형 생강에서도 여섯 면의 비율을 유지한다.
                float radius = Mathf.Sqrt(Mathf.Pow(bounds.extents.x * Mathf.Cos(angle), 2f) + Mathf.Pow(bounds.extents.z * Mathf.Sin(angle), 2f)) * 0.68f; // 껍질과 돌출 부분을 벗겨낼 거리다.
                float at = side ? center.x + radius : center.y + (step == 6 ? 1f : -1f) * bounds.extents.y * 0.72f; // 안쪽 속살을 남기는 절단 위치다.
                body.Split(axis, at, out SliceMesh low, out SliceMesh high); // 실제 삼각형을 자르고 절단면을 닫는다.
                SliceMesh kept = step == 7 ? high : low; // 아래쪽을 깎는 마지막 단계만 위쪽을 남긴다.
                SliceMesh scrap = step == 7 ? low : high; // 잘려 나간 껍질 부분이다.
                Quaternion undo = Quaternion.Inverse(rotation); // 원래 작업 방향으로 되돌릴 회전이다.
                if (kept == null || scrap == null) // 메시와 교차하지 않는 면은 본체를 보존한다.
                {
                    GingerRotateMesh(body, center, undo); // 실패한 절단의 회전만 원복한다.
                    continue; // 다음 방향을 시도한다.
                }
                GingerRotateMesh(kept, center, undo); // 남은 본체의 방향을 되돌린다.
                GingerRotateMesh(scrap, center, undo); // 껍질 조각도 같은 방향으로 되돌린다.
                HandController.Instance?.PlayChopGesture(); // 손의 칼질 동작을 재생한다.
                (HandCarry.Instance?.Current as ToolItem)?.PlayChopMotion(); // 들고 있는 칼을 움직인다.
                yield return StartCoroutine(ChopPulse()); // 칼질이 끝나는 순간 절단 결과를 적용한다.
                float removedGrams = amountGrams * Mathf.Clamp01(scrap.Volume / Mathf.Max(kept.Volume + scrap.Volume, 0.000001f)); // 잘려 나간 분량만 계산한다.
                amountGrams -= removedGrams; // 본체에 남은 무게를 갱신한다.
                body = kept; // 다음 절단은 줄어든 본체에 적용한다.
                Mesh next = GingerLocalMesh(body, frame); // 본체 로컬 좌표로 환산한다.
                filter.sharedMesh = next; // 눈에 보이는 모양을 실제로 교체한다.
                sharedMesh = next; // 이후 썰기에서도 바뀐 모양을 사용한다.
                rend.sharedMaterials = materials; // 절단면에 노란 재질을 배정한다.
                if (generated != null) Destroy(generated); // 직전 임시 메시를 해제한다.
                generated = next; // 현재 메시만 유지한다.
                DropScrap("껍질", new Color(0.7f, 0.55f, 0.35f), removedGrams); // 기존 음식물 쓰레기 처리에 연결한다.
                yield return new WaitForSeconds(0.15f); // 각 면이 깎이는 모습을 잠시 보여준다.
            }
            for (int i = 0; i < materials.Length; i++) materials[i] = flesh; // 다듬은 생강에 갈색 껍질이 남지 않도록 속살 재질로 마무리한다.
            rend.sharedMaterials = materials; // 다지기 조각도 노란 속살 재질을 물려받는다.
            cutResultMesh = null; // 후속 썰기가 기존 껍질 모델로 되돌리지 않게 한다.
            cutResultMaterial = flesh; // 후속 절단용 재질도 속살로 맞춘다.
            if (GetComponent<Collider>() is BoxCollider box) { box.center = sharedMesh.bounds.center; box.size = sharedMesh.bounds.size; } // 줄어든 크기에 집기 영역을 맞춘다.
            IsTrimmed = true; // 중복 다듬기를 막고 후속 썰기를 허용한다.
        }
        finally
        {
            Destroy(frame.gameObject); // 작업 좌표계를 정리한다.
            SupportHandController.Instance?.Hide(); // 받치던 손을 원래대로 돌린다.
            isCutting = false; // 재료 입력 잠금을 해제한다.
        }
    }

    private static void GingerRotateMesh(SliceMesh mesh, Vector3 center, Quaternion rotation) // 슬라이서의 축을 바꾸기 위한 좌표 변환이다.
    {
        for (int i = 0; i < mesh.v.Count; i++) // 모든 정점과 법선을 같은 회전으로 변환한다.
        {
            mesh.v[i] = center + rotation * (mesh.v[i] - center); // 중심을 유지하며 정점을 돌린다.
            mesh.n[i] = rotation * mesh.n[i]; // 조명 방향도 함께 돌린다.
        }
        Vector3 inside = mesh.Bounds.center; // 속살 중심을 기준으로 바깥 방향을 판단한다.
        foreach (var triangles in mesh.sub) // 원본 모델의 뒤집힌 면도 함께 바로잡는다.
        for (int i = 0; i + 2 < triangles.Count; i += 3) // 반복 절단 중 생긴 공선 점 때문에 면이 뒤집히는 것을 막는다.
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2]; // 삼각형의 세 정점이다.
            Vector3 normal = Vector3.Cross(mesh.v[b] - mesh.v[a], mesh.v[c] - mesh.v[a]); // 실제 감김 방향을 계산한다.
            if (Vector3.Dot(normal, (mesh.v[a] + mesh.v[b] + mesh.v[c]) / 3f - inside) < 0f) // 속살 안쪽을 향하는 면은 뒤집는다.
            {
                triangles[i + 1] = c; // 두 정점의 순서를 바꾼다.
                triangles[i + 2] = b; // 절단면이 겉에서 보이게 한다.
            }
        }
    }

    private Mesh GingerLocalMesh(SliceMesh body, Transform frame) // 절단 결과를 기존 재료 오브젝트 좌표로 돌려놓는다.
    {
        Mesh mesh = body.ToMesh(Vector3.zero, "Ginger_PeeledHex"); // 평평한 절단면을 가진 메시를 만든다.
        Vector3[] vertices = mesh.vertices; // 정점 좌표를 가져온다.
        Vector3[] normals = mesh.normals; // 면 법선을 가져온다.
        Matrix4x4 matrix = transform.worldToLocalMatrix * frame.localToWorldMatrix; // 작업 좌표에서 재료 좌표로 변환한다.
        Matrix4x4 normalMatrix = matrix.inverse.transpose; // 비균등 스케일에서도 올바른 법선을 유지한다.
        for (int i = 0; i < vertices.Length; i++) // 각 정점을 변환한다.
        {
            vertices[i] = matrix.MultiplyPoint3x4(vertices[i]); // 위치를 변환한다.
            normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized; // 법선을 변환한다.
        }
        mesh.vertices = vertices; // 변환된 좌표를 적용한다.
        mesh.normals = normals; // 변환된 법선을 적용한다.
        mesh.RecalculateNormals(); // 잘린 면의 방향에 맞춰 조명을 다시 계산한다.
        mesh.RecalculateBounds(); // 새 크기를 계산한다.
        return mesh; // 본체에 적용할 메시를 반환한다.
    }
}
