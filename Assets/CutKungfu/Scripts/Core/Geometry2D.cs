using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>
    /// 判定は XZ 平面(横視点寄りの 2.5D)で行う。高さは見た目の演出に留め、当たりには使わない。
    /// </summary>
    public static class Geometry2D
    {
        /// <summary>点 p と線分 ab の距離の二乗。</summary>
        public static float DistanceSqPointSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lenSq = math.lengthsq(ab);
            float t = lenSq < 1e-8f ? 0f : math.saturate(math.dot(p - a, ab) / lenSq);
            return math.distancesq(p, a + ab * t);
        }

        /// <summary>半径 bodyRadius の円が、太さ radius のカプセル(線分 ab)と重なるか。</summary>
        public static bool CapsuleOverlapsCircle(float2 a, float2 b, float radius, float2 center, float bodyRadius)
        {
            float r = radius + bodyRadius;
            return DistanceSqPointSegment(center, a, b) <= r * r;
        }

        /// <summary>軸平行の矩形の内側か。</summary>
        public static bool InsideRect(float2 p, float2 center, float2 halfExtents)
        {
            float2 d = math.abs(p - center);
            return d.x <= halfExtents.x && d.y <= halfExtents.y;
        }

        /// <summary>円が矩形にめり込んでいたら、最短方向へ押し出した中心を返す。</summary>
        public static float2 PushOutOfRect(float2 p, float radius, float2 center, float2 halfExtents)
        {
            float2 ext = halfExtents + radius;
            float2 d = p - center;
            float2 overlap = ext - math.abs(d);
            if (overlap.x <= 0f || overlap.y <= 0f) return p;
            if (overlap.x < overlap.y) p.x = center.x + math.sign(d.x == 0 ? 1f : d.x) * ext.x;
            else p.y = center.y + math.sign(d.y == 0 ? 1f : d.y) * ext.y;
            return p;
        }

        /// <summary>水平面 y=planeY と光線の交点。平行・後方なら false。</summary>
        public static bool RayPlaneY(float3 origin, float3 dir, float planeY, out float3 hit)
        {
            hit = default;
            if (math.abs(dir.y) < 1e-5f) return false;
            float t = (planeY - origin.y) / dir.y;
            if (t < 0f) return false;
            hit = origin + dir * t;
            return true;
        }
    }
}
