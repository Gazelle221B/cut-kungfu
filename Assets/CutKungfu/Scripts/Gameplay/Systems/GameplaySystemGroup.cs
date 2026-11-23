using Unity.Entities;
using Unity.Transforms;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// ゲームロジック全体。毎フレーム1回、変換の確定(TransformSystemGroup)より前に回る。
    /// 中の順序は各システムの UpdateAfter で決める:
    /// Session → GameClock → FighterTimers → CutControl → CutExecution → PlayerControl → EnemyAI
    /// → Damage → BodyMotion → Dying → StrikeFx → CharacterColor
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class GameplaySystemGroup : ComponentSystemGroup
    {
    }
}
