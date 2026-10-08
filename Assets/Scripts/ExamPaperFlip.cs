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

    void Awake()
    {
        homePosition = transform.position;
        homeScale = transform.localScale;
        SetFace(false);
    }

    public void OnClicked()
    {
        switch (state)
        {
            case ViewState.Small:
                state = ViewState.ZoomedFront;
                SetFace(false);
                StopAllCoroutines();
                StartCoroutine(TweenTo(zoomedPosition, homeScale * zoomedScaleMultiplier));
                break;

            case ViewState.ZoomedFront:
                state = ViewState.ZoomedBack;
                SetFace(true);
                break;

            case ViewState.ZoomedBack:
                state = ViewState.Small;
                SetFace(false);
                StopAllCoroutines();
                StartCoroutine(TweenTo(homePosition, homeScale));
                break;
        }
    }

    private void SetFace(bool showBack)
    {
        if (paperRenderer == null) return;
        Texture tex = showBack ? backTexture : frontTexture;
        if (tex != null) paperRenderer.material.mainTexture = tex;
    }

    private IEnumerator TweenTo(Vector3 targetPos, Vector3 targetScale)
    {
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        float t = 0f;

        while (t < tweenDuration)
        {
            t += Time.deltaTime;
            float e = Mathf.Clamp01(t / tweenDuration);
            e = e * e * (3f - 2f * e);
            transform.position = Vector3.Lerp(startPos, targetPos, e);
            transform.localScale = Vector3.Lerp(startScale, targetScale, e);
            yield return null;
        }

        transform.position = targetPos;
        transform.localScale = targetScale;
    }
}
