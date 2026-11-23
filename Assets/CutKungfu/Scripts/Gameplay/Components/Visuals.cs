using Unity.Entities;

namespace CutKungfu.Gameplay
{
    /// <summary>
    /// 根(足元)とは別に、色を変える見た目の子実体。根は足元にあり、倒れるときは足元を軸に回る。
    /// </summary>
    public struct VisualBody : IComponentData
    {
        public Entity Body;
    }
}
