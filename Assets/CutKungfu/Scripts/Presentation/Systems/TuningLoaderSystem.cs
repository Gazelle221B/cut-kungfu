using System;
using System.IO;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using UnityEngine;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// 調整値を StreamingAssets/CutKungfu/tuning.json から読み、変更を監視して即時反映する(TDD 12章)。
    /// ビルド後もテキストを書き換えるだけで数値を試せる。ファイルが無ければ既定値を書き出す。
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(GameplayBootstrapSystem))]
    public partial class TuningLoaderSystem : SystemBase
    {
        const float PollInterval = 1f;

        [Serializable]
        sealed class TuningFile
        {
            public Tuning tuning = Tuning.Default;
        }

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, "CutKungfu", "tuning.json");

        DateTime _lastWrite;
        float _nextPoll;
        int _version;

        protected override void OnCreate() => RequireForUpdate<TuningSingleton>();

        protected override void OnStartRunning() => TryLoad(force: true);

        protected override void OnUpdate()
        {
            _nextPoll -= UnityEngine.Time.unscaledDeltaTime;
            if (_nextPoll > 0f) return;
            _nextPoll = PollInterval;
            TryLoad(force: false);
        }

        void TryLoad(bool force)
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    if (!force) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, ToJson(Tuning.Default));
                }

                var write = File.GetLastWriteTimeUtc(path);
                if (!force && write == _lastWrite) return;
                _lastWrite = write;

                var file = new TuningFile();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), file);
                SystemAPI.SetSingleton(new TuningSingleton { Value = file.tuning, Version = ++_version });
                Debug.Log($"[CutKungfu] 調整値を読み込みました (v{_version}): {path}");
            }
            catch (Exception e)
            {
                // 書きかけの JSON などで落とさない。前の値のまま続ける
                Debug.LogWarning($"[CutKungfu] 調整値の読み込みに失敗しました。前の値を使います: {e.Message}");
            }
        }

        public static string ToJson(in Tuning tuning) => JsonUtility.ToJson(new TuningFile { tuning = tuning }, prettyPrint: true);
    }
}
