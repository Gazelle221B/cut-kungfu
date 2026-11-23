using System.Collections.Generic;

namespace CutKungfu.Core
{
    /// <summary>
    /// <see cref="ICutMotionSource"/> の仮実装。計画した瞬間に準備完了になり、
    /// 呼び出し側が望んだ長さで <see cref="CutMotion.Sample"/> の補間を返す。接触は区間の終点。
    /// </summary>
    public sealed class ProceduralCutMotionSource : ICutMotionSource
    {
        readonly Dictionary<int, MotionSegment> _plans = new();
        int _next = MotionHandle.None;

        /// <summary>生きている計画の数(捨て忘れの検出用)。</summary>
        public int ActivePlans => _plans.Count;

        public int Plan(in MotionSegment segment, in MotionContext context)
        {
            do { _next = _next == int.MaxValue ? 1 : _next + 1; } while (_plans.ContainsKey(_next));
            _plans[_next] = segment;
            return _next;
        }

        public bool IsReady(int handle) => _plans.ContainsKey(handle);

        public float Duration(int handle) => _plans.TryGetValue(handle, out var s) ? s.PreferredDuration : 0f;

        public float ContactTime(int handle) => Duration(handle);

        public MotionSample Sample(int handle, float time)
            => _plans.TryGetValue(handle, out var s) ? CutMotion.Sample(s, s.PreferredDuration, time) : default;

        public void Discard(int handle) => _plans.Remove(handle);
    }
}
