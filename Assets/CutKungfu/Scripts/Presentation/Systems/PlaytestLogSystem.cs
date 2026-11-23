using System;
using System.IO;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using Unity.Entities;
using UnityEngine;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// プレイテスト記録を CSV に追記し、セッション内の集計(<see cref="PlaytestStats"/>)を更新する。
    /// エディタではプロジェクト直下の Playtests/、ビルドでは persistentDataPath/Playtests/ に出す。
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlaytestLogSystem : SystemBase
    {
        StreamWriter _writer;
        bool _failed;

        public PlaytestStats Stats { get; } = new();
        public string FilePath { get; private set; }

        protected override void OnCreate() => RequireForUpdate<TelemetryEvent>();

        protected override void OnDestroy()
        {
            _writer?.Dispose();
            _writer = null;
        }

        protected override void OnUpdate()
        {
            var events = SystemAPI.GetSingletonBuffer<TelemetryEvent>();
            if (events.Length == 0) return;

            EnsureWriter();
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i].Value;
                Stats.Add(e);
                if (_writer == null) continue;
                try
                {
                    _writer.WriteLine(PlaytestCsv.ToRow(e));
                }
                catch (Exception ex)
                {
                    // 書けなくても集計は続ける。同じ出来事を二重に数えないよう、イベントは必ず消費する
                    Debug.LogWarning($"[CutKungfu] プレイテスト記録の書き込みに失敗しました。以降は画面の集計のみ: {ex.Message}");
                    _writer.Dispose();
                    _writer = null;
                    _failed = true;
                }
            }
            events.Clear();
        }

        void EnsureWriter()
        {
            if (_writer != null || _failed) return;
            try
            {
                string dir = Application.isEditor
                    ? Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Playtests")
                    : Path.Combine(Application.persistentDataPath, "Playtests");
                Directory.CreateDirectory(dir);
                FilePath = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                _writer = new StreamWriter(FilePath, append: false) { AutoFlush = true };
                _writer.WriteLine(PlaytestCsv.Header);
                Debug.Log($"[CutKungfu] プレイテスト記録: {FilePath}");
            }
            catch (Exception e)
            {
                _failed = true;
                Debug.LogWarning($"[CutKungfu] プレイテスト記録を開けません: {e.Message}");
            }
        }
    }
}
