using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Click to zoom the paper in for reading; click again to flip to the other
/// side (swaps the actual exam-sheet page image); click a third time to
/// shrink back down onto the wall.
/// </summary>
public class ExamPaperFlip : MonoBehaviour, IClickable
{
    [SerializeField] private string dishName = "요리";

    /// <summary>The seasonings this dish's actual exam sheet lists as supplied
    /// material (지급재료) - drives which SeasoningBottles are allowed to
    /// appear on the shelf at all (see SauceShelfController). Using anything
    /// not listed on the real sheet is grounds for disqualification, so this
    /// must match this exam page's own content exactly, not just "whatever
    /// happens to be on the shelf already".</summary>
    [SerializeField] private string[] requiredSeasonings;
    public IReadOnlyList<string> RequiredSeasonings => requiredSeasonings;

    [SerializeField] private Renderer paperRenderer;
    [SerializeField] private Texture frontTexture;
    [SerializeField] private Texture backTexture;
    [SerializeField] private Vector3 zoomedPosition = new Vector3(0f, -0.5f, -2f);
    [SerializeField] private float zoomedScaleMultiplier = 4.5f;
    [SerializeField] private float tweenDuration = 0.25f;

    public string DishName => dishName;

    private enum ViewState { Small, ZoomedFront, ZoomedBack }
    private ViewState state = ViewState.Small;

    private Vector3 homePosition;
    private Vector3 homeScale;
    private Quaternion homeRotation;

    void Awake()
    {
        homePosition = transform.position;
        homeScale = transform.localScale;
        homeRotation = transform.rotation;
        SetFace(false);
    }

    public void OnClicked()
    {
        switch (state)
        {
            case ViewState.Small:
                // (크게 펼친 시험지는 전체 화면 기준 자리 - 확대 중이면 먼저 전체 보기로)
                if (KitchenCamera.IsZoomed) { KitchenCamera.Instance.ShowOverview(); return; }
                state = ViewState.ZoomedFront;
                SetFace(false);
                StopAllCoroutines();
                OpenForReading();
                break;

            case ViewState.ZoomedFront:
                state = ViewState.ZoomedBack;
                SetFace(true);
                break;

            case ViewState.ZoomedBack:
                state = ViewState.Small;
                SetFace(false);
                StopAllCoroutines();
                StartCoroutine(TweenTo(homePosition, homeScale, homeRotation));
                break;
        }
    }

    private void SetFace(bool showBack)
    {
        if (paperRenderer == null) return;
        Texture tex = showBack ? backTexture : frontTexture;
        if (tex != null) paperRenderer.material.mainTexture = tex;
    }

    // 원본 시험지를 카메라 정면에 펼쳐 작은 글씨가 기울어지지 않게 읽는다.
    private void OpenForReading()
    {
        Camera camera = Camera.main;
        MeshFilter mesh = paperRenderer != null ? paperRenderer.GetComponent<MeshFilter>() : null;
        if (camera == null || mesh == null || mesh.sharedMesh == null)
        {
            StartCoroutine(TweenTo(zoomedPosition, homeScale * zoomedScaleMultiplier, homeRotation));
            return;
        }
        float depth = Mathf.Max(camera.nearClipPlane + 1f, 3f);
        float height = camera.orthographic ? camera.orthographicSize * 2f
            : 2f * depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        Vector3 size = Vector3.Scale(mesh.sharedMesh.bounds.size, Vector3.Scale(homeScale, parentScale));
        float factor = Mathf.Min(height * 0.92f / Mathf.Max(Mathf.Abs(size.y), 0.001f),
            height * camera.aspect * 0.92f / Mathf.Max(Mathf.Abs(size.x), 0.001f));
        Vector3 at = camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, depth));
        StartCoroutine(TweenTo(at, homeScale * factor, camera.transform.rotation));
    }

    private IEnumerator TweenTo(Vector3 targetPos, Vector3 targetScale, Quaternion targetRotation)
    {
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        Quaternion startRotation = transform.rotation;
        float t = 0f;

        while (t < tweenDuration)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / tweenDuration);
            e = e * e * (3f - 2f * e);
            transform.position = Vector3.Lerp(startPos, targetPos, e);
            transform.localScale = Vector3.Lerp(startScale, targetScale, e);
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, e);
            yield return null;
        }

        transform.position = targetPos;
        transform.localScale = targetScale;
        transform.rotation = targetRotation;
    }
}
