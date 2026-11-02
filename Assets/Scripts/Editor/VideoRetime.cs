using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 녹화 영상 줄이기 (발표용, 플레이 모드에서 실행): 원본 mp4를 VideoPlayer로 원하는 시각마다
/// 한 장씩 읽어 (1) 미리보기 표(장면 찾기용) 또는 (2) 구간별 배속을 다르게 한 새 mp4로 만든다.
/// 소리는 넣지 않는다. ffmpeg 없이 Unity만으로.
/// </summary>
public static class VideoRetime
{
    private static VideoPlayer vp;                 // 원본을 읽는 플레이어
    private static RenderTexture rt;               // 읽은 화면
    private static Texture2D tex;                  // 화면을 옮겨 담을 그림
    private static readonly Queue<double> times = new Queue<double>(); // 읽을 시각들 (초)
    private static Action<Texture2D, double> onFrame; // 한 장 읽을 때마다
    private static Action onDone;                  // 다 읽으면
    private static int settle;                     // 이동 후 화면이 바뀌길 기다리는 프레임 수
    private static bool seeking;                   // 이동 중
    private static double current;                 // 지금 읽는 시각
    public static string Status = "";              // 진행 상황 (밖에서 확인)

    /// <summary>원본 정보: 길이(초) · 초당 프레임 · 크기.</summary>
    public static void Info(string url) => Open(url, 320, 180, () =>
    {
        Status = $"length={vp.length:0.0} fps={vp.frameRate:0.##} size={vp.width}x{vp.height}"; // 정보 기록
        Close();                                                                                // 닫기
    });

    /// <summary>미리보기 표: every초마다 한 장, cols열로 붙여 png로 저장.</summary>
    public static void ContactSheet(string url, string pngPath, double from, double to, double every, int cols, int thumbW, int thumbH)
    {
        var shots = new List<Texture2D>();                                    // 찍은 작은 그림들
        Open(url, thumbW, thumbH, () =>
        {
            for (double t = from; t < Math.Min(to, vp.length); t += every) times.Enqueue(t); // 찍을 시각들
            onFrame = (img, t) =>
            {
                var copy = new Texture2D(thumbW, thumbH, TextureFormat.RGB24, false); // 한 장 복사
                copy.SetPixels(img.GetPixels()); copy.Apply();
                shots.Add(copy);
            };
            onDone = () =>
            {
                int rows = Mathf.CeilToInt(shots.Count / (float)cols);              // 줄 수
                var sheet = new Texture2D(cols * thumbW, rows * thumbH, TextureFormat.RGB24, false); // 큰 표
                for (int i = 0; i < shots.Count; i++)
                    sheet.SetPixels((i % cols) * thumbW, (rows - 1 - i / cols) * thumbH, thumbW, thumbH, shots[i].GetPixels()); // 왼쪽 위부터 차례로
                sheet.Apply();
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());                  // 저장
                Status = $"sheet saved {shots.Count} shots";
                Close();
            };
            Next();                                                                 // 시작
        });
    }

    /// <summary>잘라 보는 미리보기 표: 원본을 rtW×rtH로 읽어 (cx,cy,cw,ch) 부분만 cols열로 붙임 (장면 찾기용).</summary>
    public static void ContactSheetCrop(string url, string pngPath, double from, double to, double every, int cols,
                                        int rtW, int rtH, int cx, int cy, int cw, int ch)
    {
        var shots = new List<Color[]>();                                      // 잘라 낸 조각들
        Open(url, rtW, rtH, () =>
        {
            for (double t = from; t < Math.Min(to, vp.length); t += every) times.Enqueue(t); // 찍을 시각들
            onFrame = (img, t) => shots.Add(img.GetPixels(cx, cy, cw, ch));    // 그 부분만
            onDone = () =>
            {
                int rows = Mathf.CeilToInt(shots.Count / (float)cols);          // 줄 수
                var sheet = new Texture2D(cols * cw, rows * ch, TextureFormat.RGB24, false); // 큰 표
                for (int i = 0; i < shots.Count; i++)
                    sheet.SetPixels((i % cols) * cw, (rows - 1 - i / cols) * ch, cw, ch, shots[i]); // 왼쪽 위부터
                sheet.Apply();
                File.WriteAllBytes(pngPath, sheet.EncodeToPNG());              // 저장
                Status = $"sheet saved {shots.Count} shots";
                Close();
            };
            Next();
        });
    }

    /// <summary>구간별 배속으로 새 mp4: segments = (원본 시작초, 원본 끝초, 배속)들. 출력 fps 고정.</summary>
    public static void Retime(string url, string mp4Path, int width, int height, int fps, IList<Vector3> segments)
    {
        MediaEncoder encoder = null;                                               // mp4 쓰기
        Open(url, width, height, () =>
        {
            foreach (Vector3 s in segments)                                        // 구간마다
                for (double t = s.x; t < Math.Min(s.y, vp.length); t += s.z / fps) times.Enqueue(t); // 출력 한 프레임 = 원본 배속/fps초
            var attrs = new VideoTrackAttributes
            {
                frameRate = new MediaRational(fps), width = (uint)width, height = (uint)height, includeAlpha = false,
            };
            encoder = new MediaEncoder(mp4Path, attrs);                            // 새 파일
            int total = times.Count, n = 0;                                        // 진행률
            onFrame = (img, t) => { encoder.AddFrame(img); n++; Status = $"{n}/{total} (src {t:0.0}s)"; }; // 한 프레임 쓰기
            onDone = () => { encoder.Dispose(); Status = $"done {n} frames"; Close(); };                  // 마무리
            Next();
        });
    }

    // ---- 내부 ----

    private static void Open(string url, int w, int h, Action ready)
    {
        Close();                                                                   // 이전 것 정리
        times.Clear();
        rt = new RenderTexture(w, h, 0);                                           // 원본을 이 크기로 그림
        tex = new Texture2D(w, h, TextureFormat.RGBA32, false);                    // 읽어 올 그림
        var go = new GameObject("VideoRetimePlayer");                              // 임시 플레이어
        vp = go.AddComponent<VideoPlayer>();
        vp.source = VideoSource.Url; vp.url = url;                                 // 원본 파일
        vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = rt;      // 그림으로
        vp.audioOutputMode = VideoAudioOutputMode.None;                            // 소리 안 씀
        vp.playOnAwake = false; vp.skipOnDrop = false; vp.waitForFirstFrame = true;
        vp.sendFrameReadyEvents = true;                                            // 프레임이 그려질 때마다 알림
        vp.frameReady += (src, idx) =>                                             // 프레임이 실제로 그려졌을 때
        {
            if (!seeking || src.time < current - 0.05) return;                       // 원하는 시각 전 프레임은 무시
            seeking = false; settle = 2; src.Pause();                              // 멈추고 조금 뒤 읽기
        };
        vp.prepareCompleted += _ => { vp.Pause(); ready(); };                      // 준비되면 시작
        vp.errorReceived += (_, msg) => { Status = "error: " + msg; Close(); };    // 오류
        vp.Prepare();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;                                          // 매 프레임 진행
        Status = "preparing";
    }

    private static void Next()
    {
        if (times.Count == 0) { onDone?.Invoke(); return; }                        // 다 읽음
        current = times.Dequeue();                                                 // 다음 시각
        seeking = true;
        vp.time = current;                                                         // 그 시각으로 이동
        vp.Play();                                                                 // 재생해야 그 프레임이 실제로 그려짐 (멈춘 채 이동하면 검은 화면)
    }

    private static void Tick()
    {
        if (vp == null || seeking || settle <= 0) return;                          // 이동 중이면 기다림
        if (--settle > 0) return;                                                  // 화면이 바뀌길 조금 더
        var prev = RenderTexture.active;
        RenderTexture.active = rt;                                                 // 읽은 화면에서
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();    // 한 장 복사
        RenderTexture.active = prev;
        onFrame?.Invoke(tex, current);                                             // 넘겨줌
        Next();                                                                    // 다음 장
    }

    private static void Close()
    {
        EditorApplication.update -= Tick;                                          // 진행 멈춤
        if (vp != null) UnityEngine.Object.Destroy(vp.gameObject);                 // 플레이어 정리
        vp = null;
        if (rt != null) rt.Release();
        onFrame = null; onDone = null;
    }
}
