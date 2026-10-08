using UnityEngine;

/// <summary>
/// The examiner's announcements (TTS) during the 순서도 screen before the
/// exam: the clips played one after another, in order (tts1 → tts8), while
/// that screen is up. Paused when it's closed (시작하세요), carried on if it's
/// opened again; played through once only.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PlanAnnouncer : MonoBehaviour
{
    [Tooltip("Played in this order.")]
    [SerializeField] private AudioClip[] clips;
    [Tooltip("Pause between one announcement and the next (seconds).")]
    [SerializeField] private float gap = 0.8f;
    [Tooltip("Wait before the first one (seconds).")]
    [SerializeField] private float startDelay = 1f;
    [Range(0f, 1f)] [SerializeField] private float volume = 1f;

    private AudioSource source;
    private int index = -1;   // the clip playing (or last played)
    private float waitUntil;
    private bool paused;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // (a voice over the room, not from a spot in it)
        source.volume = volume;
        waitUntil = Time.time + startDelay;
    }

    void Update()
    {
        if (clips == null || clips.Length == 0) return;
        bool showing = CookingPlanManager.Instance != null && CookingPlanManager.Instance.IsShowing;

        if (!showing)
        {
            if (source.isPlaying) { source.Pause(); paused = true; }
            return;
        }
        if (paused) { source.UnPause(); paused = false; return; }
        if (source.isPlaying) return;

        if (index >= clips.Length - 1) return;        // all said
        if (index >= 0 && waitUntil < 0f) waitUntil = Time.time + gap; // one just ended: the pause
        if (Time.time < waitUntil) return;

        index++;
        waitUntil = -1f;
        if (clips[index] == null) return;
        source.clip = clips[index];
        source.Play();
    }
}
