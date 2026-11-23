using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// ECS の状態を <see cref="HudSnapshot"/> に詰め、<see cref="HudView"/> に描かせる。
    /// 手応えの出来事(FeedbackEvent)もここで読み出して空にし、効果音へも回す。
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CameraRigSystem))]
    public partial class HudSystem : SystemBase
    {
        readonly HudSnapshot _snapshot = new();
        HudView _view;
        SfxBank _sfx;
        bool _statsVisible;

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<SessionState>();
            RequireForUpdate<ScenarioWave>();
        }

        protected override void OnDestroy() => _sfx?.Dispose();

        protected override void OnUpdate()
        {
            var root = PresentationRoot.Current;
            if (root == null || root.Hud == null) return;
            if (_view == null || !_view.IsBoundTo(root.Hud)) _view = new HudView(root.Hud);
            _sfx ??= new SfxBank();

            Fill(_snapshot);

            var input = World.GetExistingSystemManaged<InputBridgeSystem>();
            if (input != null && input.StatsToggledThisFrame) _statsVisible = !_statsVisible;
            var log = World.GetExistingSystemManaged<PlaytestLogSystem>();

            _view.Render(_snapshot, root.Camera, UnityEngine.Time.unscaledDeltaTime, _statsVisible ? log?.Stats : null, log?.FilePath);
            foreach (var e in _snapshot.Feedback) _sfx.Play(root.SfxSource, e);
        }

        void Fill(HudSnapshot s)
        {
            s.Clear();
            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var session = SystemAPI.GetSingleton<SessionState>();
            s.Wave = session.WaveIndex;
            s.WaveCount = SystemAPI.GetSingletonBuffer<ScenarioWave>(true).Length;
            s.Take = session.Take;
            s.CommitEnabled = session.CommitEnabled;
            s.Phase = session.Phase;
            s.Score = session.Score;
            s.LastNgReason = session.LastNgReason;
            s.LastCutPoses = session.LastCutPoses;
            s.LastCommitTime = session.LastCommitTime;
            s.MinGaugeToEnter = CutRules.MinimumGaugeToEnterSlow(tuning);
            s.CostPerPose = tuning.CostPerPose;
            s.MaxPoses = tuning.MaxPoses;

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var fighter = SystemAPI.GetComponent<Fighter>(player);
            var gauge = SystemAPI.GetComponent<FilmGauge>(player);
            var cut = SystemAPI.GetComponent<CutState>(player);
            int placed = SystemAPI.GetBuffer<PlacedPose>(player).Length;

            s.PlayerPosition = SystemAPI.GetComponent<LocalTransform>(player).Position;
            s.Health = fighter.Health;
            s.MaxHealth = fighter.MaxHealth;
            s.Gauge = gauge.Value;
            s.GaugeMax = gauge.Max;
            s.CutPhase = cut.Phase;
            s.SelectedKind = cut.SelectedKind;
            s.Placed = placed;
            s.SlowRemaining01 = cut.Phase == CutPhase.Slow ? 1f - cut.SlowRealElapsed / tuning.SlowMaxRealSeconds : 0f;
            s.PreviewResult = cut.PreviewResult;
            s.CommitIfReleased = CutRules.CommitTime(tuning, placed, session.CommitEnabled);
            s.CommitIfOneMore = CutRules.CommitTime(tuning, placed + 1, session.CommitEnabled);
            s.PowerIfReleased = CutRules.PowerMultiplier(tuning, placed);
            s.CommitRemaining = cut.CommitRemaining;
            s.CommitDuration = cut.CommitDuration;

            // 今解除したら: 置いた順に辿る時間 + 硬直。その間に殴ってくる敵を「危険」として示す
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            float exec = 0f;
            float3 from = s.PlayerPosition;
            for (int i = 0; i < poses.Length; i++)
            {
                exec += CutRules.SegmentDuration(tuning, math.distance(from.xz, poses[i].Position.xz));
                from = poses[i].Position;
            }
            s.CommitPosition = from;
            s.VulnerableUntil = cut.Phase == CutPhase.Slow && placed > 0 ? exec + s.CommitIfReleased : 0f;

            foreach (var (brain, lt) in SystemAPI.Query<RefRO<EnemyBrain>, RefRO<LocalTransform>>().WithAll<EnemyTag>().WithNone<Dying>())
            {
                var marker = new HudSnapshot.EnemyMarker
                {
                    Position = lt.ValueRO.Position,
                    Eta = brain.ValueRO.NextAttackEta(tuning, math.distance(lt.ValueRO.Position.xz, ((float3)s.PlayerPosition).xz)),
                    Windup = brain.ValueRO.State == EnemyState.Windup,
                    Staggered = brain.ValueRO.State == EnemyState.Staggered,
                };
                marker.Threat = s.VulnerableUntil > 0f && marker.Eta < s.VulnerableUntil;
                s.Enemies.Add(marker);
            }

            foreach (var (ghost, lt) in SystemAPI.Query<RefRO<GhostPose>, RefRO<LocalTransform>>())
            {
                if (ghost.ValueRO.IsPreview) continue;
                s.Ghosts.Add(new HudSnapshot.GhostMarker { Position = lt.ValueRO.Position, Order = ghost.ValueRO.Order });
            }

            var feedback = SystemAPI.GetSingletonBuffer<FeedbackEvent>();
            for (int i = 0; i < feedback.Length; i++) s.Feedback.Add(feedback[i]);
            feedback.Clear();
        }
    }
}
