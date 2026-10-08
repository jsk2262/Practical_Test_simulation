using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The examiner's voiced briefing after the 순서도 screen (ExamBriefing): one
/// recording (TTS) and the subtitles for it, each shown from its start time
/// (seconds into the recording) until the next one starts. Times and wording
/// are set here in the Inspector to match the recording.
/// </summary>
public class BriefingVoice : MonoBehaviour
{
    [Serializable]
    public class Cue
    {
        [Tooltip("Seconds into the recording this subtitle shows from.")]
        public float start;
        [TextArea(1, 4)] public string text;
    }

    [SerializeField] private AudioClip clip;
    [SerializeField] private List<Cue> cues = new List<Cue>();

    public AudioClip Clip => clip;
    public IReadOnlyList<Cue> Cues => cues;
    public bool Ready => clip != null && cues.Count > 0;

    public static BriefingVoice Find() => FindFirstObjectByType<BriefingVoice>();

    /// <summary>Which subtitle is up at this point of the recording (-1: none yet).</summary>
    public int CueAt(float time)
    {
        int at = -1;
        for (int i = 0; i < cues.Count; i++) if (cues[i].start <= time) at = i;
        return at;
    }
}
