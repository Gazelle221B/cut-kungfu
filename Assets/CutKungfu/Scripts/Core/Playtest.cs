using System.Globalization;
using System.Text;

namespace CutKungfu.Core
{
    public enum PlaytestEventKind : byte
    {
        /// <summary>カットを解除し、硬直まで終えた(主指標の1サンプル)。</summary>
        Cut = 0,
        /// <summary>スローに入ったが1個も置かずに解除した。</summary>
        CutCancelled,
        PlayerHit,
        NG,
        WaveClear,
        ConditionChanged,
        WaveStart,
    }

    /// <summary>
    /// プレイテストの記録1行。GDD 14章の主指標「敵の数ごとの平均ポーズ数」と、
    /// 悪化させないもの「スローの使用回数」を後から集計できる粒度で残す。
    /// blittable なので ECS のイベントバッファにそのまま載る。
    /// </summary>
    public struct PlaytestEvent
    {
        public PlaytestEventKind Kind;
        public double Time;
        public int Wave;
        public int FormationEnemies;
        public int Take;
        public bool CommitEnabled;
        /// <summary>スロー突入時点で生きていた敵の数。</summary>
        public int EnemiesAlive;
        /// <summary>スロー突入時点で半径4m以内にいた敵の数(包囲の強さ)。</summary>
        public int EnemiesNear;
        public int Poses;
        /// <summary><see cref="BrickSequence"/> で詰めた置いた順。</summary>
        public int Sequence;
        /// <summary>スロー突入時のゲージ(固定費を払う前)。</summary>
        public float GaugeBefore;
        /// <summary>そのゲージで置けた最大のポーズ数。poses と比べて「置かなかった」と「置けなかった」を分ける。</summary>
        public int MaxAffordablePoses;
        public float CommitTime;
        public int Kills;
        public bool HitDuringCommit;
        public float Hae;
        public float PlayerHealth;

        // --- 対立仮説「置く操作が面倒なだけ」を切り分けるための操作の記録 ---
        /// <summary>スローに居た実時間[s]。</summary>
        public float SlowSeconds;
        /// <summary>スローの制限時間切れで解除されたか。</summary>
        public bool TimedOut;
        /// <summary>置いたポーズを戻した回数。</summary>
        public int Undos;
        /// <summary>置こうとして置けなかった回数(机以外に机の技、ゲージ不足など)。</summary>
        public int Rejects;
        /// <summary>スロー突入時に最も早く殴ってくる敵までの時間(ゲーム秒)。包囲の切迫度。</summary>
        public float MinEnemyEta;
        /// <summary>技の実行中に殴られて中断したか。</summary>
        public bool Interrupted;
        /// <summary>この技で、範囲技の打撃が上限(AreaDamageCapRatio)に掛かって削られた回数。</summary>
        public int AreaCapHits;
    }

    public static class PlaytestCsv
    {
        public const string Header =
            "kind,time,wave,formation_enemies,take,commit_enabled,enemies_alive,enemies_near,poses,sequence," +
            "gauge_before,max_affordable,commit_time,kills,hit_during_commit,hae,player_hp," +
            "slow_seconds,timed_out,undos,rejects,min_enemy_eta,interrupted,area_cap_hits";

        public static string KindCode(PlaytestEventKind k) => k switch
        {
            PlaytestEventKind.Cut => "cut",
            PlaytestEventKind.CutCancelled => "cut_cancelled",
            PlaytestEventKind.PlayerHit => "player_hit",
            PlaytestEventKind.NG => "ng",
            PlaytestEventKind.WaveClear => "wave_clear",
            PlaytestEventKind.ConditionChanged => "condition_changed",
            PlaytestEventKind.WaveStart => "wave_start",
            _ => k.ToString(),
        };

        public static string ToRow(in PlaytestEvent e)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(128);
            sb.Append(KindCode(e.Kind)).Append(',')
              .Append(e.Time.ToString("F3", c)).Append(',')
              .Append(e.Wave).Append(',')
              .Append(e.FormationEnemies).Append(',')
              .Append(e.Take).Append(',')
              .Append(e.CommitEnabled ? 1 : 0).Append(',')
              .Append(e.EnemiesAlive).Append(',')
              .Append(e.EnemiesNear).Append(',')
              .Append(e.Poses).Append(',')
              .Append(BrickSequence.ToCodeString(e.Sequence)).Append(',')
              .Append(e.GaugeBefore.ToString("F1", c)).Append(',')
              .Append(e.MaxAffordablePoses).Append(',')
              .Append(e.CommitTime.ToString("F2", c)).Append(',')
              .Append(e.Kills).Append(',')
              .Append(e.HitDuringCommit ? 1 : 0).Append(',')
              .Append(e.Hae.ToString("F1", c)).Append(',')
              .Append(e.PlayerHealth.ToString("F1", c)).Append(',')
              .Append(e.SlowSeconds.ToString("F2", c)).Append(',')
              .Append(e.TimedOut ? 1 : 0).Append(',')
              .Append(e.Undos).Append(',')
              .Append(e.Rejects).Append(',')
              .Append(e.MinEnemyEta.ToString("F2", c)).Append(',')
              .Append(e.Interrupted ? 1 : 0).Append(',')
              .Append(e.AreaCapHits);
            return sb.ToString();
        }
    }

    /// <summary>
    /// セッション中の集計。条件(コミット時間 ON/OFF)×包囲人数(1, 2, 3+)ごとの平均ポーズ数。
    /// </summary>
    public sealed class PlaytestStats
    {
        public const int Buckets = 3; // 1体, 2体, 3体以上

        readonly int[,] _cuts = new int[2, Buckets];
        readonly int[,] _poses = new int[2, Buckets];
        readonly int[] _slowUses = new int[2];
        readonly int[] _ngs = new int[2];
        readonly int[] _hitsDuringCommit = new int[2];

        public static int BucketOf(int enemies) => System.Math.Clamp(enemies, 1, Buckets) - 1;

        public void Add(in PlaytestEvent e)
        {
            int c = e.CommitEnabled ? 1 : 0;
            switch (e.Kind)
            {
                case PlaytestEventKind.Cut:
                    int b = BucketOf(e.EnemiesAlive);
                    _cuts[c, b]++;
                    _poses[c, b] += e.Poses;
                    _slowUses[c]++;
                    if (e.HitDuringCommit) _hitsDuringCommit[c]++;
                    break;
                case PlaytestEventKind.CutCancelled:
                    _slowUses[c]++;
                    break;
                case PlaytestEventKind.NG:
                    _ngs[c]++;
                    break;
            }
        }

        public int CutCount(bool commitEnabled, int bucket) => _cuts[commitEnabled ? 1 : 0, bucket];

        /// <summary>平均ポーズ数。サンプルが無ければ false。</summary>
        public bool TryAveragePoses(bool commitEnabled, int bucket, out float average)
        {
            int c = commitEnabled ? 1 : 0;
            int n = _cuts[c, bucket];
            average = n == 0 ? 0f : (float)_poses[c, bucket] / n;
            return n > 0;
        }

        public int SlowUses(bool commitEnabled) => _slowUses[commitEnabled ? 1 : 0];
        public int NgCount(bool commitEnabled) => _ngs[commitEnabled ? 1 : 0];
        public int HitsDuringCommit(bool commitEnabled) => _hitsDuringCommit[commitEnabled ? 1 : 0];
    }
}
