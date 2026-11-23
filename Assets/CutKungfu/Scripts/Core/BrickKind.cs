namespace CutKungfu.Core
{
    /// <summary>
    /// プレイヤーが空間に置く決めポーズ(ポーズブリック)の種類。GDD 8章の初版4種。
    /// 値は配列添字・テレメトリの符号化に使うため、追加は末尾に限る。
    /// </summary>
    public enum BrickKind : byte
    {
        Palm = 0,         // 掌底: 正面の単体に短く当てる
        Spin = 1,         // 回し蹴り: 周囲を払う
        FlyingKick = 2,   // 飛び蹴りの頂点: 距離を詰める(経路上も当たる)
        TableLanding = 3, // 机上の着地: 机の上にだけ置ける。着地で範囲攻撃
    }

    public static class BrickKinds
    {
        public const int Count = 4;

        public static string DisplayName(BrickKind kind) => kind switch
        {
            BrickKind.Palm => "掌底",
            BrickKind.Spin => "回し蹴り",
            BrickKind.FlyingKick => "飛び蹴り",
            BrickKind.TableLanding => "机上の着地",
            _ => kind.ToString(),
        };

        public static string Code(BrickKind kind) => kind switch
        {
            BrickKind.Palm => "palm",
            BrickKind.Spin => "spin",
            BrickKind.FlyingKick => "fly",
            BrickKind.TableLanding => "table",
            _ => "?",
        };
    }
}
