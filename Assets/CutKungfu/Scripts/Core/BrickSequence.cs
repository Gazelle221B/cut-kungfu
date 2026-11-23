using System.Text;

namespace CutKungfu.Core
{
    /// <summary>
    /// 1回のカットで置いたブリック列(最大4)を int 1つに詰める。
    /// ECS のイベントに文字列を載せずに済ませるための符号化。各桁は (kind+1)、基数8。
    /// </summary>
    public static class BrickSequence
    {
        const int Base = 8;

        public static int Append(int packed, BrickKind kind) => packed * Base + ((int)kind + 1);

        public static int RemoveLast(int packed) => packed / Base;

        public static int Count(int packed)
        {
            int n = 0;
            while (packed > 0) { packed /= Base; n++; }
            return n;
        }

        /// <summary>"palm>spin>fly" の形にする(置いた順)。</summary>
        public static string ToCodeString(int packed)
        {
            if (packed <= 0) return string.Empty;
            var digits = new System.Collections.Generic.List<int>(4);
            while (packed > 0) { digits.Add(packed % Base - 1); packed /= Base; }
            var sb = new StringBuilder();
            for (int i = digits.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0) sb.Append('>');
                sb.Append(BrickKinds.Code((BrickKind)digits[i]));
            }
            return sb.ToString();
        }
    }
}
