using Unity.Entities;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// ゲームロジック全体。固定タイムステップ(<see cref="StepSeconds"/>)で回す。表示のフレームレートが変わっても、
    /// 1回の更新の長さは同じなので、判定のタイミングや吹き飛びの位置が揃う。
    /// FixedStepSimulationSystemGroup は SimulationSystemGroup の先頭で回るので、変換の確定(TransformSystemGroup)より前になる。
    /// 中の順序は各システムの UpdateAfter で決める:
    /// Session → GameClock → FighterTimers → CutControl → CutExecution → PlayerControl → EnemyAI
    /// → Damage → BodyMotion → Dying → StrikeFx → CharacterColor → PlayerInputConsume
    /// </summary>
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial class GameplaySystemGroup : ComponentSystemGroup
    {
        /// <summary>ゲームロジックの1回の更新の長さ[秒](60Hz)。</summary>
        public const float StepSeconds = 1f / 60f;
    }

    /// <summary>固定タイムステップの長さを <see cref="GameplaySystemGroup.StepSeconds"/> に合わせる(最初のフレームで1回だけ)。</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class GameplayStepRateSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var group = World.GetExistingSystemManaged<FixedStepSimulationSystemGroup>();
            if (group != null) group.Timestep = GameplaySystemGroup.StepSeconds;
            Enabled = false;
        }
    }
}
