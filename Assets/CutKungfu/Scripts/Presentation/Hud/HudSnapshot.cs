using System.Collections.Generic;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using UnityEngine;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// HUD が描くものを1フレーム分まとめた表示用モデル。ECS を知らない <see cref="HudView"/> へ渡す。
    /// 毎フレーム作り直さず、使い回して GC を出さない。
    /// </summary>
    public sealed class HudSnapshot
    {
        public struct EnemyMarker
        {
            public Vector3 Position;
            public float Eta;
            public bool Windup;
            public bool Staggered;
        }

        public struct GhostMarker
        {
            public Vector3 Position;
            public int Order;
        }

        // 進行
        public int Wave;
        public int WaveCount;
        public int Take;
        public bool CommitEnabled;
        public SessionPhase Phase;
        public float Score;
        public NgReason LastNgReason;
        public int LastCutPoses;
        public float LastCommitTime;

        // 主人公
        public Vector3 PlayerPosition;
        public float Health;
        public float MaxHealth;
        public float Gauge;
        public float GaugeMax;
        public CutPhase CutPhase;
        public BrickKind SelectedKind;
        public int Placed;
        public int MaxPoses;
        public float SlowRemaining01;
        public PlacementResult PreviewResult;
        public float CommitIfReleased;
        public float CommitIfOneMore;
        public float PowerIfReleased;
        public float CommitRemaining;
        public float CommitDuration;
        public float MinGaugeToEnter;
        public float CostPerPose;

        public readonly List<EnemyMarker> Enemies = new();
        public readonly List<GhostMarker> Ghosts = new();
        public readonly List<FeedbackEvent> Feedback = new();

        public void Clear()
        {
            Enemies.Clear();
            Ghosts.Clear();
            Feedback.Clear();
        }
    }
}
