using System;
using System.Collections.Generic;
using CutKungfu.Gameplay;
using UnityEngine;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// 試作用の効果音。音源ファイルを用意せず、起動時に短い波形を合成する(カチンコ・チャイム・打撃など)。
    /// 本番の音に差し替えるときは <see cref="Play"/> の中身だけを置き換えればよい。
    /// </summary>
    public sealed class SfxBank : IDisposable
    {
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> _clips = new();
        readonly System.Random _rng = new(7);

        public SfxBank()
        {
            _clips["clap"] = Make("clap", 0.12f, t => Noise() * Env(t, 0.002f, 0.06f) + Square(t, 1800f) * 0.3f * Env(t, 0f, 0.01f));
            _clips["chime"] = Make("chime", 0.35f, t => (Sine(t, 1320f) + 0.4f * Sine(t, 2640f)) * Env(t, 0.003f, 0.25f) * 0.5f);
            _clips["undo"] = Make("undo", 0.15f, t => Sine(t, 660f - t * 1500f) * Env(t, 0.002f, 0.1f) * 0.4f);
            _clips["reject"] = Make("reject", 0.18f, t => Square(t, 180f) * Env(t, 0.002f, 0.12f) * 0.25f);
            _clips["hit"] = Make("hit", 0.12f, t => (Noise() * 0.6f + Sine(t, 140f)) * Env(t, 0.001f, 0.07f) * 0.6f);
            _clips["cuthit"] = Make("cuthit", 0.25f, t => (Noise() * 0.7f + Sine(t, 90f + 60f * Env(t, 0f, 0.05f))) * Env(t, 0.001f, 0.16f) * 0.8f);
            _clips["parry"] = Make("parry", 0.4f, t => (Sine(t, 1900f) + Sine(t, 2533f)) * Env(t, 0.001f, 0.3f) * 0.35f);
            _clips["hurt"] = Make("hurt", 0.2f, t => (Noise() * 0.5f + Square(t, 110f) * 0.5f) * Env(t, 0.001f, 0.15f) * 0.6f);
            _clips["release"] = Make("release", 0.22f, t => Noise() * Env(t, 0.03f, 0.12f) * (0.3f + t * 2f) * 0.6f);
            _clips["ng"] = Make("ng", 0.6f, t => Square(t, 98f) * Env(t, 0.01f, 0.45f) * 0.35f);
            _clips["ok"] = Make("ok", 0.6f, t => (t < 0.18f ? Sine(t, 880f) : Sine(t, 1320f)) * Env(t, 0.005f, 0.5f) * 0.4f);
        }

        public void Play(AudioSource source, in FeedbackEvent e)
        {
            if (source == null) return;
            switch (e.Kind)
            {
                case FeedbackKind.SlowEnter: Fire(source, "clap", 1f); break;
                case FeedbackKind.PosePlaced: Fire(source, "chime", 1f + 0.12f * (e.Extra - 1)); break;
                case FeedbackKind.PoseUndone: Fire(source, "undo", 1f); break;
                case FeedbackKind.PoseRejected: Fire(source, "reject", 1f); break;
                case FeedbackKind.CutRelease: Fire(source, "release", 1f); break;
                case FeedbackKind.CutCancelled: Fire(source, "undo", 0.8f); break;
                case FeedbackKind.Hit: Fire(source, "hit", 0.95f + (float)_rng.NextDouble() * 0.1f); break;
                case FeedbackKind.CutHit: Fire(source, "cuthit", 0.95f + (float)_rng.NextDouble() * 0.1f); break;
                case FeedbackKind.CutHitCapped: Fire(source, "hit", 0.75f); break;
                case FeedbackKind.Kill: Fire(source, "cuthit", 0.7f); break;
                case FeedbackKind.Parry: Fire(source, "parry", 1f); break;
                case FeedbackKind.PlayerHurt: Fire(source, "hurt", 1f); break;
                case FeedbackKind.NG: Fire(source, "ng", 1f); Fire(source, "clap", 0.9f); break;
                case FeedbackKind.Clear: Fire(source, "ok", 1f); break;
                case FeedbackKind.TakeStart: Fire(source, "clap", 1.1f); break;
            }
        }

        void Fire(AudioSource source, string id, float pitch)
        {
            if (!_clips.TryGetValue(id, out var clip)) return;
            source.pitch = pitch;
            source.PlayOneShot(clip);
        }

        AudioClip Make(string name, float seconds, Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create($"sfx_{name}", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);
        static float Sine(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Square(float t, float hz) => Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));
        static float Env(float t, float attack, float decay) => t < attack ? t / Mathf.Max(attack, 1e-4f) : Mathf.Exp(-(t - attack) / Mathf.Max(decay, 1e-4f) * 3f);

        public void Dispose()
        {
            foreach (var c in _clips.Values)
                if (c != null) UnityEngine.Object.Destroy(c);
            _clips.Clear();
        }
    }
}
