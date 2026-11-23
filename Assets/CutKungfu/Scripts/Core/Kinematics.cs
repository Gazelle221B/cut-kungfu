using Unity.Mathematics;

namespace CutKungfu.Core
{
    /// <summary>
    /// 時間で閉じた形の積分。1回の更新の長さ(dt)を変えても、同じ時間が経てば同じ位置・速度になる。
    /// 「毎フレーム速度に係数を掛ける」「速度を足してから位置を進める」は dt が大きいほど遠くへ進むので使わない。
    /// </summary>
    public static class Kinematics
    {
        /// <summary>
        /// 指数減衰する速度(v' = -damping·v)で dt だけ進む。戻り値は移動量、velocity は dt 後の速度に更新する。
        /// </summary>
        public static float3 DampedStep(ref float3 velocity, float damping, float dt)
        {
            if (damping <= 1e-6f) return velocity * dt;
            float decay = math.exp(-damping * dt);
            float3 displacement = velocity * ((1f - decay) / damping);
            velocity *= decay;
            return displacement;
        }

        /// <summary>一定の加速度で dt だけ進む(重力の落下)。戻り値は移動量、velocity は dt 後の速度に更新する。</summary>
        public static float AcceleratedStep(ref float velocity, float acceleration, float dt)
        {
            float displacement = velocity * dt + 0.5f * acceleration * dt * dt;
            velocity += acceleration * dt;
            return displacement;
        }
    }
}
