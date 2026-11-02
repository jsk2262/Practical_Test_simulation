using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 발표용 영상 편집 (플레이 모드에서 실행): 여러 영상의 구간을 차례로 이어 붙여 새 mp4를 만든다.
/// 구간마다 배속을 따로 정하고, 두 영상을 왼쪽 · 오른쪽 2분할로 나란히 보여 줄 수도 있다.
/// 소리 · 자막 없음. ffmpeg 없이 Unity만으로 (VideoPlayer로 읽고 MediaEncoder로 씀).
/// </summary>
public static class VideoCompose
{
    /// <summary>한 장면: url의 from~to초를 speed배속으로. url2가 있으면 2분할(오른쪽).</summary>
    public class Clip
    {
        public string url; public double from, to, speed;      // 왼쪽(또는 전체) 영상
        public string url2; public double from2, to2, speed2;  // 오른쪽 영상 (2분할일 때만)
    }

    /// <summary>영상 하나를 원하는 시각마다 한 장씩 읽는 도우미.</summary>
    private class Reader
    {
        public VideoPlayer vp;            // 원본 플레이어
        public RenderTexture rt;          // 읽은 화면
        public Texture2D tex;             // 화면을 옮겨 담을 그림
        public bool prepared;             // 준비 끝
        public bool hasFrame;             // 원하는 시각의 그림을 읽어 둠
        private bool waiting;             // 원하는 프레임을 기다리는 중
        private int settle;               // 그려진 뒤 조금 기다릴 프레임 수
        private double want;              // 원하는 시각

        public Reader(string url, int w, int h)
        {
            rt = new RenderTexture(w, h, 0);                                       // 이 크기로 그림
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);                // 읽어 올 그림
            vp = new GameObject("VideoComposeReader").AddComponent<VideoPlayer>(); // 임시 플레이어
            vp.source = VideoSource.Url; vp.url = url;                             // 원본 파일
            vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = rt;  // 그림으로
            vp.audioOutputMode = VideoAudioOutputMode.None;                        // 소리 안 씀
            vp.playOnAwake = false; vp.skipOnDrop = false; vp.waitForFirstFrame = true;
            vp.sendFrameReadyEvents = true;                                        // 프레임이 그려질 때마다 알림
            vp.frameReady += (src, idx) =>
            {
                if (!waiting || src.time < want - 0.05) return;                    // 원하는 시각 전 프레임은 무시
                waiting = false; settle = 2; src.Pause();                          // 멈추고 조금 뒤 읽기
            };
            vp.prepareCompleted += _ => { vp.Pause(); prepared = true; };          // 준비 끝
            vp.Prepare();
        }

        public void Seek(double t)
        {
            want = Math.Max(0, Math.Min(t, vp.length - 0.1));                      // 영상이 먼저 끝나면 마지막 장면에 멈춤
            hasFrame = false; waiting = true;
            vp.time = want; vp.Play();                                             // 재생해야 그 프레임이 실제로 그려짐
        }

        public void Tick()
        {
            if (waiting || hasFrame || settle <= 0) return;                        // 기다리는 중
            if (--settle > 0) return;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;                                             // 읽은 화면에서
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();// 한 장 복사
            RenderTexture.active = prev;
            hasFrame = true;
        }

        public void Dispose()
        {
            if (vp != null) UnityEngine.Object.Destroy(vp.gameObject);             // 플레이어 정리
            if (rt != null) rt.Release();
        }
    }

    private static List<Clip> clips;     // 이어 붙일 장면들
    private static int clipIndex;        // 지금 장면
    private static int frame, frames;    // 지금 장면의 몇 번째 프레임 / 전체 프레임 수
    private static Reader a, b;          // 왼쪽(전체) · 오른쪽 영상
    private static MediaEncoder encoder; // 새 mp4
    private static Texture2D outTex;     // 내보낼 한 장
    private static int W, H, FPS;        // 출력 크기 · 초당 프레임
    private static bool requested;       // 이번 프레임 읽기를 요청함
    private static int written;          // 지금까지 쓴 프레임 수
    public static string Status = "";    // 진행 상황 (밖에서 확인)

    public static void Run(string mp4Path, int width, int height, int fps, List<Clip> list)
    {
        W = width; H = height; FPS = fps; clips = list; clipIndex = -1; written = 0;
        encoder = new MediaEncoder(mp4Path, new VideoTrackAttributes
        {
            frameRate = new MediaRational(fps), width = (uint)width, height = (uint)height, includeAlpha = false,
        });                                                                        // 새 파일
        outTex = new Texture2D(width, height, TextureFormat.RGBA32, false);        // 내보낼 그림
        NextClip();                                                                // 첫 장면
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;                                          // 매 프레임 진행
    }

    private static void NextClip()
    {
        a?.Dispose(); b?.Dispose(); a = b = null;                                  // 이전 장면 정리
        clipIndex++;
        if (clipIndex >= clips.Count)                                              // 다 이어 붙임
        {
            EditorApplication.update -= Tick;
            encoder.Dispose();                                                     // 파일 마무리
            Status = $"done {written} frames";
            return;
        }
        Clip c = clips[clipIndex];
        a = new Reader(c.url, W, H);                                               // 왼쪽(전체) 영상
        if (c.url2 != null) b = new Reader(c.url2, W, H);                          // 오른쪽 영상
        double n1 = (c.to - c.from) / c.speed * FPS;                               // 왼쪽 프레임 수
        double n2 = c.url2 != null ? (c.to2 - c.from2) / c.speed2 * FPS : 0;       // 오른쪽 프레임 수
        frames = Mathf.CeilToInt((float)Math.Max(n1, n2));                         // 긴 쪽에 맞춤
        frame = 0; requested = false;
    }

    private static void Tick()
    {
        if (a == null || !a.prepared || (b != null && !b.prepared)) return;        // 준비 기다림
        Clip c = clips[clipIndex];
        if (!requested)                                                            // 이번 프레임 읽기 시작
        {
            a.Seek(c.from + frame * c.speed / FPS);                                // 왼쪽(전체) 시각
            if (b != null) b.Seek(c.from2 + frame * c.speed2 / FPS);               // 오른쪽 시각
            requested = true;
            return;
        }
        a.Tick(); b?.Tick();                                                       // 그림 읽기
        if (!a.hasFrame || (b != null && !b.hasFrame)) return;                     // 다 읽을 때까지
        if (b == null) outTex.SetPixels(a.tex.GetPixels());                        // 한 화면 그대로
        else
        {
            int half = W / 2, q = W / 4;                                           // 반쪽 폭 · 가운데 자를 시작점
            outTex.SetPixels(0, 0, half, H, a.tex.GetPixels(q, 0, half, H));       // 왼쪽: 첫 영상 가운데 부분
            outTex.SetPixels(half, 0, half, H, b.tex.GetPixels(q, 0, half, H));    // 오른쪽: 둘째 영상 가운데 부분
            for (int y = 0; y < H; y++) for (int x = half - 2; x < half + 2; x++) outTex.SetPixel(x, y, Color.black); // 가운데 구분선
        }
        outTex.Apply();
        encoder.AddFrame(outTex);                                                  // 새 파일에 한 장
        written++; frame++; requested = false;
        Status = $"clip {clipIndex + 1}/{clips.Count} frame {frame}/{frames} (total {written})";
        if (frame >= frames) NextClip();                                           // 다음 장면
    }
}
