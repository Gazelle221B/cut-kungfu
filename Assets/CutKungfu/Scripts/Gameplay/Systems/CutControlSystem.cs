using CutKungfu.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 「止める」と「置く」。スローへの突入、照準プレビュー、ポーズの配置・取り消し、解除(切る)の受付まで。
    /// 解除後の動きは CutExecutionSystem が担う。
    /// </summary>
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(FighterTimersSystem))]
    public partial struct CutControlSystem : ISystem
    {
        /// <summary>包囲の強さを数える半径[m](テレメトリ用)。</summary>
        const float NearRadius = 4f;

        static readonly float4 PreviewValid = new float4(0.55f, 0.95f, 1f, 0.28f);
        static readonly float4 PreviewInvalid = new float4(1f, 0.3f, 0.25f, 0.22f);
        static readonly float4 PlacedColor = new float4(0.6f, 0.95f, 1f, 0.5f);

        /// <summary>
        /// 解除したのに動きの計画が揃わないとき、スローのまま待つ上限(実時間)。超えたら未準備の区間を代替で作り直す。
        /// 仮実装(手続き補間)は即座に揃うので、この経路は MotionBricks の非同期推論のためのもの。
        /// </summary>
        public const float ReleaseWaitTimeoutReal = 0.5f;

        EntityQuery _aliveEnemies;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<PlayerTag>();
            state.RequireForUpdate<PrefabLibrary>();
            state.RequireForUpdate<SessionState>();
            _aliveEnemies = new EntityQueryBuilder(Allocator.Temp).WithAll<EnemyTag, LocalTransform>().WithNone<Dying>().Build(ref state);
        }

        // 動作生成ソース(managed)を呼ぶので Burst にしない
        public void OnUpdate(ref SystemState state)
        {
            var session = SystemAPI.GetSingleton<SessionState>();
            if (session.Phase != SessionPhase.Playing) return;

            var input = SystemAPI.GetSingleton<PlayerInput>();
            var tuning = SystemAPI.GetSingleton<TuningSingleton>().Value;
            var clock = SystemAPI.GetSingleton<GameClock>();
            var table = SystemAPI.TryGetSingleton<TableSingleton>(out var t) ? t.Rect : TableRect.None;

            var player = SystemAPI.GetSingletonEntity<PlayerTag>();
            var cut = SystemAPI.GetComponent<CutState>(player);
            var gauge = SystemAPI.GetComponent<FilmGauge>(player);
            var fighter = SystemAPI.GetComponent<Fighter>(player);
            var action = SystemAPI.GetComponent<PlayerAction>(player);
            float3 playerPos = SystemAPI.GetComponent<LocalTransform>(player).Position;
            cut.InputConsumed = false;

            if (input.SelectBrick >= 0 && input.SelectBrick < BrickKinds.Count) cut.SelectedKind = (BrickKind)input.SelectBrick;
            if (input.CycleBrick != 0) cut.SelectedKind = (BrickKind)(((int)cut.SelectedKind + input.CycleBrick + BrickKinds.Count) % BrickKinds.Count);

            if (cut.Phase == CutPhase.None)
            {
                bool canAct = action.State == PlayerActionState.Free && fighter.HitStun <= 0f;
                if (input.Cut && canAct)
                {
                    if (CutRules.CanEnterSlow(tuning, gauge.Value))
                    {
                        EnterSlow(ref state, ref cut, ref gauge, session, clock, tuning, playerPos);
                        cut.InputConsumed = true;
                        SystemAPI.SetComponent(player, new PlayerAction());
                        SystemAPI.SetComponent(player, new Knockback());
                    }
                    else
                    {
                        Feedback(ref state, FeedbackKind.PoseRejected, playerPos, (int)PlacementResult.NotEnoughGauge);
                    }
                }
            }
            else if (cut.Phase == CutPhase.Slow)
            {
                cut.InputConsumed = true; // スロー中の押下は「置く・戻す」であって攻撃・捌きではない
                cut.SlowRealElapsed += clock.RealDeltaTime;
                // 解除待ちの間は配置を締め切る
                if (!cut.ReleasePending) UpdateSlow(ref state, player, ref cut, ref gauge, input, tuning, table, fighter.Facing, playerPos);

                bool timeUp = cut.SlowRealElapsed >= tuning.SlowMaxRealSeconds;
                if (!cut.ReleasePending && (input.Cut || timeUp))
                {
                    cut.ReleasePending = true;
                    cut.ReleaseWaitReal = 0f;
                    cut.Record.TimedOut = timeUp && !input.Cut;
                }
                else if (cut.ReleasePending)
                {
                    cut.ReleaseWaitReal += clock.RealDeltaTime;
                }

                if (cut.ReleasePending && PlansReadyOrReplaced(ref state, player, ref cut, tuning, fighter.Facing, playerPos))
                    Release(ref state, player, ref cut, session, tuning, playerPos);
            }

            SystemAPI.SetComponent(player, cut);
            SystemAPI.SetComponent(player, gauge);
        }

        void EnterSlow(ref SystemState state, ref CutState cut, ref FilmGauge gauge, in SessionState session,
            in GameClock clock, in Tuning tuning, float3 playerPos)
        {
            var waves = SystemAPI.GetSingletonBuffer<ScenarioWave>(true);
            int near = 0;
            float minEta = float.MaxValue;
            foreach (var (lt, brain) in SystemAPI.Query<RefRO<LocalTransform>, RefRO<EnemyBrain>>().WithAll<EnemyTag>().WithNone<Dying>())
            {
                float dist = math.distance(lt.ValueRO.Position.xz, playerPos.xz);
                if (dist <= NearRadius) near++;
                minEta = math.min(minEta, brain.ValueRO.NextAttackEta(tuning, dist));
            }

            cut.Record = new PlaytestEvent
            {
                Kind = PlaytestEventKind.Cut,
                Time = clock.ElapsedReal,
                Wave = session.WaveIndex,
                FormationEnemies = session.WaveIndex < waves.Length ? waves[session.WaveIndex].SpawnCount : 0,
                Take = session.Take,
                CommitEnabled = session.CommitEnabled,
                EnemiesAlive = _aliveEnemies.CalculateEntityCount(),
                EnemiesNear = near,
                GaugeBefore = gauge.Value,
                MaxAffordablePoses = CutRules.AffordablePoses(tuning, gauge.Value - tuning.SlowFixedCost, 0),
                MinEnemyEta = minEta == float.MaxValue ? 0f : minEta,
            };
            gauge.Add(-tuning.SlowFixedCost);
            cut.Phase = CutPhase.Slow;
            cut.SlowRealElapsed = 0f;
            cut.PreviewResult = PlacementResult.Ok;
            Feedback(ref state, FeedbackKind.SlowEnter, playerPos, 0);
        }

        void UpdateSlow(ref SystemState state, Entity player, ref CutState cut, ref FilmGauge gauge, in PlayerInput input,
            in Tuning tuning, in TableRect table, float3 facing, float3 playerPos)
        {
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            int placed = poses.Length;
            float3 anchor = placed > 0 ? poses[placed - 1].Position : playerPos;
            var brick = tuning.GetBrick(cut.SelectedKind);
            float3 aim = Aim(input, table, anchor, facing, brick.Reach);
            // 壁の外には置けない(凸な部屋なので、照準を部屋に収めれば到達距離で丸めても外に出ない)
            if (SystemAPI.TryGetSingleton<ArenaBounds>(out var arena))
            {
                float r = tuning.PlayerRadius;
                aim.x = math.clamp(aim.x, arena.Min.x + r, arena.Max.x - r);
                aim.z = math.clamp(aim.z, arena.Min.y + r, arena.Max.y - r);
            }

            var result = CutRules.ValidatePlacement(tuning, cut.SelectedKind, placed, gauge.Value, anchor, aim, table, out float3 pos);
            float2 dir = pos.xz - anchor.xz;
            float yaw = math.lengthsq(dir) > 1e-4f ? math.atan2(dir.x, dir.y) : math.atan2(facing.x, facing.z);

            if (input.Primary)
            {
                if (result == PlacementResult.Ok)
                {
                    gauge.Add(-tuning.CostPerPose);
                    var lib = SystemAPI.GetSingleton<PrefabLibrary>();
                    var ghost = SpawnGhost(ref state, lib.Ghost(cut.SelectedKind), pos, yaw, placed, false, PlacedColor);
                    cut.Record.Sequence = BrickSequence.Append(cut.Record.Sequence, cut.SelectedKind);
                    var placedPoses = SystemAPI.GetBuffer<PlacedPose>(player);
                    int repeat = 0;
                    for (int i = 0; i < placedPoses.Length; i++)
                        if (placedPoses[i].Kind == cut.SelectedKind) repeat++;
                    // 置いた時点で動きを計画する(スロー中に生成を進め、解除時の待ちを隠す)
                    float anchorYaw = placedPoses.Length > 0 ? placedPoses[placedPoses.Length - 1].Yaw : CutMotion.YawOf(facing);
                    int handle = state.World.GetOrCreateSystemManaged<CutMotionService>().Primary.Plan(
                        CutMotionPlanning.Segment(tuning, anchor, anchorYaw, cut.SelectedKind, pos, yaw),
                        CutMotionPlanning.Context(anchor, anchorYaw));
                    placedPoses.Add(new PlacedPose
                    {
                        Kind = cut.SelectedKind, Position = pos, Yaw = yaw, RepeatIndex = repeat, MotionHandle = handle, Ghost = ghost,
                    });
                    Feedback(ref state, FeedbackKind.PosePlaced, pos, placed + 1);
                    placed++;
                    // 置いた直後は次のアンカーで判定し直す
                    result = CutRules.ValidatePlacement(tuning, cut.SelectedKind, placed, gauge.Value, pos, aim, table, out pos);
                }
                else
                {
                    cut.Record.Rejects++;
                    Feedback(ref state, FeedbackKind.PoseRejected, pos, (int)result);
                }
            }
            else if (input.Secondary && placed > 0)
            {
                var last = poses[placed - 1];
                state.World.GetOrCreateSystemManaged<CutMotionService>().For(last).Discard(last.MotionHandle);
                poses.RemoveAt(placed - 1);
                if (state.EntityManager.Exists(last.Ghost)) state.EntityManager.DestroyEntity(last.Ghost);
                gauge.Add(tuning.CostPerPose);
                cut.Record.Sequence = BrickSequence.RemoveLast(cut.Record.Sequence);
                cut.Record.Undos++;
                Feedback(ref state, FeedbackKind.PoseUndone, last.Position, placed - 1);
                placed--;
            }

            UpdatePreview(ref state, ref cut, pos, yaw, result);
        }

        static float3 Aim(in PlayerInput input, in TableRect table, float3 anchor, float3 facing, float reach)
        {
            if (math.lengthsq(input.AimStick) > 0.04f)
                return anchor + new float3(input.AimStick.x, 0f, input.AimStick.y) * reach;

            if (input.AimRayValid)
            {
                if (table.Exists
                    && Geometry2D.RayPlaneY(input.AimRayOrigin, input.AimRayDirection, table.TopY, out var onTop)
                    && table.Contains(onTop.xz))
                    return onTop;
                if (Geometry2D.RayPlaneY(input.AimRayOrigin, input.AimRayDirection, 0f, out var onGround))
                    return onGround;
            }
            return anchor + facing * 2f;
        }

        void UpdatePreview(ref SystemState state, ref CutState cut, float3 pos, float yaw, PlacementResult result)
        {
            var em = state.EntityManager;
            bool hidden = result == PlacementResult.TooManyPoses;
            if (cut.Preview != Entity.Null && (hidden || cut.PreviewKind != cut.SelectedKind || !em.Exists(cut.Preview)))
            {
                if (em.Exists(cut.Preview)) em.DestroyEntity(cut.Preview);
                cut.Preview = Entity.Null;
            }
            cut.PreviewResult = result;
            cut.PreviewPosition = pos;
            if (hidden) return;

            var color = result == PlacementResult.Ok ? PreviewValid : PreviewInvalid;
            if (cut.Preview == Entity.Null)
            {
                var lib = SystemAPI.GetSingleton<PrefabLibrary>();
                cut.Preview = SpawnGhost(ref state, lib.Ghost(cut.SelectedKind), pos, yaw, -1, true, color);
                cut.PreviewKind = cut.SelectedKind;
            }
            else
            {
                em.SetComponentData(cut.Preview, LocalTransform.FromPositionRotation(pos, quaternion.RotateY(yaw)));
                Tint(em, cut.Preview, color);
            }
        }

        /// <summary>
        /// 解除してよいか。計画が揃っていれば true。揃っていなければ待ち、待ちの上限を超えたら
        /// 未準備の区間を代替(必ずすぐ準備できる)で計画し直して true を返す。
        /// </summary>
        bool PlansReadyOrReplaced(ref SystemState state, Entity player, ref CutState cut, in Tuning tuning, float3 facing, float3 playerPos)
        {
            var sources = state.World.GetOrCreateSystemManaged<CutMotionService>();
            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            if (sources.AllReady(poses)) return true;
            if (cut.ReleaseWaitReal < ReleaseWaitTimeoutReal) return false;

            for (int i = 0; i < poses.Length; i++)
            {
                var p = poses[i];
                if (sources.For(p).IsReady(p.MotionHandle)) continue;
                sources.For(p).Discard(p.MotionHandle);
                float3 from = i > 0 ? poses[i - 1].Position : playerPos;
                float fromYaw = i > 0 ? poses[i - 1].Yaw : CutMotion.YawOf(facing);
                p.MotionHandle = sources.Fallback.Plan(
                    CutMotionPlanning.Segment(tuning, from, fromYaw, p.Kind, p.Position, p.Yaw),
                    CutMotionPlanning.Context(from, fromYaw));
                p.UsesFallback = true;
                poses[i] = p;
            }
            return true;
        }

        void Release(ref SystemState state, Entity player, ref CutState cut, in SessionState session, in Tuning tuning, float3 playerPos)
        {
            cut.Record.SlowSeconds = cut.SlowRealElapsed;
            cut.ReleasePending = false;
            var em = state.EntityManager;
            if (cut.Preview != Entity.Null && em.Exists(cut.Preview)) em.DestroyEntity(cut.Preview);
            cut.Preview = Entity.Null;

            var poses = SystemAPI.GetBuffer<PlacedPose>(player);
            if (poses.Length == 0)
            {
                // 1個も置かずに解除: 固定費は戻らない(1ポーズ連打を割に合わなくする狙いと同じ理屈)
                cut.Record.Kind = PlaytestEventKind.CutCancelled;
                SystemAPI.GetSingletonBuffer<TelemetryEvent>().Add(new TelemetryEvent { Value = cut.Record });
                cut.Phase = CutPhase.None;
                Feedback(ref state, FeedbackKind.CutCancelled, playerPos, 0);
                return;
            }

            cut.PoseCount = poses.Length;
            cut.SegmentIndex = 0;
            cut.SegmentTime = 0f;
            cut.SegmentFrom = playerPos;
            cut.StrikeFired = false;
            cut.CommitDuration = CutRules.CommitTime(tuning, cut.PoseCount, session.CommitEnabled);
            cut.Record.Poses = cut.PoseCount;
            cut.Record.CommitTime = cut.CommitDuration;
            cut.Phase = CutPhase.Executing;

            var s = SystemAPI.GetSingletonRW<SessionState>();
            s.ValueRW.LastCutPoses = cut.PoseCount;
            s.ValueRW.LastCommitTime = cut.CommitDuration;

            Feedback(ref state, FeedbackKind.CutRelease, playerPos, cut.PoseCount);
        }

        static Entity SpawnGhost(ref SystemState state, Entity prefab, float3 pos, float yaw, int order, bool preview, float4 color)
        {
            var em = state.EntityManager;
            var e = em.Instantiate(prefab);
            em.SetComponentData(e, LocalTransform.FromPositionRotation(pos, quaternion.RotateY(yaw)));
            em.SetComponentData(e, new GhostPose { Order = order, IsPreview = preview });
            Tint(em, e, color);
            return e;
        }

        static void Tint(EntityManager em, Entity root, float4 color)
        {
            if (!em.HasComponent<VisualBody>(root)) return;
            var body = em.GetComponentData<VisualBody>(root).Body;
            if (body != Entity.Null && em.HasComponent<URPMaterialPropertyBaseColor>(body))
                em.SetComponentData(body, new URPMaterialPropertyBaseColor { Value = color });
        }

        void Feedback(ref SystemState state, FeedbackKind kind, float3 pos, int extra)
            => SystemAPI.GetSingletonBuffer<FeedbackEvent>().Add(new FeedbackEvent { Kind = kind, Position = pos, Extra = extra });
    }
}
