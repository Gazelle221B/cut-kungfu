using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>机(地形連携の対象)の天板。XZ の軸平行矩形と天板の高さ。</summary>
    [System.Serializable]
    public struct TableRect
    {
        public bool Exists;
        public float2 Center;
        public float2 HalfExtents;
        public float TopY;

        public static TableRect None => default;

        public readonly bool Contains(float2 xz) => Exists && Geometry2D.InsideRect(xz, Center, HalfExtents);
    }
}
