using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace CutKungfu.Presentation
{
    /// <summary>
    /// UI Toolkit の HUD。レイアウトと見た目は Hud.uxml / Hud.uss にあり、ここは値の流し込みだけを行う。
    /// GDD 10章「スロー中に必ず見えるもの」: 各敵の次の攻撃までの時間、解除したときの硬直、残りゲージ。
    /// </summary>
    public sealed class HudView
    {
        const float FrameSeconds = 0.1f; // フィルム帯の1コマ
        const float PopupLife = 0.8f;
        const float BannerLife = 1.4f;

        readonly UIDocument _document;
        readonly VisualElement _root;
        readonly VisualElement _world;
        readonly Label _wave, _take, _condition, _score, _gaugeLabel, _poseCount, _multiplier, _commitPreview, _bannerText, _bannerSub, _statsBody;
        readonly VisualElement _hpFill, _gaugeFill, _gaugeCost, _gaugeThreshold, _slowPanel, _slowTimerFill, _banner, _stats;
        readonly Label[] _bricks = new Label[BrickKinds.Count];
        readonly VisualElement[] _brickBoxes = new VisualElement[BrickKinds.Count];

        readonly LabelPool _enemyLabels;
        readonly LabelPool _ghostLabels;
        readonly VisualElement _filmStrip;
        readonly List<VisualElement> _filmFrames = new();
        readonly Label _filmLabel;
        readonly List<Popup> _popups = new();
        readonly StringBuilder _sb = new(256);

        float _bannerTimer;
        bool _bannerSticky;

        sealed class Popup
        {
            public Label Label;
            public Vector3 World;
            public float Age;
        }

        public HudView(UIDocument document)
        {
            _document = document;
            _root = document.rootVisualElement;
            ApplyJapaneseFont(_root);

            _world = _root.Q("world-layer");
            _wave = _root.Q<Label>("clapper-wave");
            _take = _root.Q<Label>("clapper-take");
            _condition = _root.Q<Label>("clapper-condition");
            _score = _root.Q<Label>("score-value");
            _hpFill = _root.Q("hp-fill");
            _gaugeFill = _root.Q("gauge-fill");
            _gaugeCost = _root.Q("gauge-cost");
            _gaugeThreshold = _root.Q("gauge-threshold");
            _gaugeLabel = _root.Q<Label>("gauge-label");
            _slowPanel = _root.Q("slow-panel");
            _slowTimerFill = _root.Q("slow-timer-fill");
            _poseCount = _root.Q<Label>("pose-count");
            _multiplier = _root.Q<Label>("multiplier");
            _commitPreview = _root.Q<Label>("commit-preview");
            _banner = _root.Q("banner");
            _bannerText = _root.Q<Label>("banner-text");
            _bannerSub = _root.Q<Label>("banner-sub");
            _stats = _root.Q("stats");
            _statsBody = _root.Q<Label>("stats-body");

            var bricks = _root.Q("bricks");
            for (int i = 0; i < BrickKinds.Count; i++)
            {
                var box = new VisualElement { pickingMode = PickingMode.Ignore };
                box.AddToClassList("brick");
                var key = new Label($"{i + 1}");
                key.AddToClassList("brick__key");
                var name = new Label(BrickKinds.DisplayName((BrickKind)i));
                name.AddToClassList("brick__name");
                box.Add(key);
                box.Add(name);
                bricks.Add(box);
                _brickBoxes[i] = box;
                _bricks[i] = name;
            }

            _enemyLabels = new LabelPool(_world, "world-label", "world-label--eta");
            _ghostLabels = new LabelPool(_world, "world-label", "world-label--ghost");

            _filmStrip = new VisualElement { pickingMode = PickingMode.Ignore };
            _filmStrip.AddToClassList("film-strip");
            _filmLabel = new Label();
            _filmLabel.AddToClassList("film-strip__label");
            _world.Add(_filmStrip);
            _filmStrip.Add(_filmLabel);
        }

        public bool IsBoundTo(UIDocument doc) => doc == _document && doc.rootVisualElement == _root;

        public void Render(HudSnapshot s, Camera cam, float dt, PlaytestStats stats, string logPath)
        {
            var c = CultureInfo.InvariantCulture;
            _wave.text = $"SCENE 1 / WAVE {s.Wave + 1}/{s.WaveCount}";
            _take.text = $"TAKE {s.Take}";
            _condition.text = s.CommitEnabled ? "条件A: コミット時間 あり (F1)" : "条件B: コミット時間 なし (F1)";
            _score.text = Mathf.RoundToInt(s.Score).ToString(c);

            SetWidth(_hpFill, s.MaxHealth > 0 ? s.Health / s.MaxHealth : 0f);
            float g01 = s.GaugeMax > 0 ? s.Gauge / s.GaugeMax : 0f;
            SetWidth(_gaugeFill, g01);
            _gaugeLabel.text = Mathf.FloorToInt(s.Gauge).ToString(c);
            _gaugeThreshold.style.left = Length.Percent(100f * Mathf.Clamp01(s.MinGaugeToEnter / Mathf.Max(1f, s.GaugeMax)));
            _gaugeThreshold.style.display = s.CutPhase == CutPhase.None ? DisplayStyle.Flex : DisplayStyle.None;

            bool slow = s.CutPhase == CutPhase.Slow;
            _slowPanel.EnableInClassList("slow-panel--visible", slow);
            if (slow)
            {
                // 次の1個を置いたときに減るゲージを青く示す
                bool canPlaceMore = s.PreviewResult == PlacementResult.Ok;
                float cost01 = canPlaceMore ? s.CostPerPose / Mathf.Max(1f, s.GaugeMax) : 0f;
                _gaugeCost.style.left = Length.Percent(100f * Mathf.Max(0f, g01 - cost01));
                _gaugeCost.style.width = Length.Percent(100f * cost01);

                SetWidth(_slowTimerFill, s.SlowRemaining01);
                _poseCount.text = $"{s.Placed} / {s.MaxPoses}";
                _multiplier.text = $"威力 ×{s.PowerIfReleased.ToString("0.0", c)}";
                _commitPreview.text = s.Placed == 0
                    ? "ポーズを置いてください"
                    : $"硬直 {s.CommitIfReleased.ToString("0.0", c)}s" + (canPlaceMore ? $" (+1で {s.CommitIfOneMore.ToString("0.0", c)}s)" : "");
                for (int i = 0; i < BrickKinds.Count; i++)
                    _brickBoxes[i].EnableInClassList("brick--selected", (int)s.SelectedKind == i);
            }
            else
            {
                _gaugeCost.style.width = 0;
            }

            RenderWorldMarkers(s, cam, slow);
            RenderFeedback(s, cam, dt);
            RenderBanner(s, dt);
            RenderStats(stats, logPath);
        }

        void RenderWorldMarkers(HudSnapshot s, Camera cam, bool slow)
        {
            var panel = _root.panel;
            if (cam == null || panel == null) return;

            // 敵の頭上: 次の攻撃までの時間。予備動作中は赤地で強調
            _enemyLabels.Begin();
            foreach (var e in s.Enemies)
            {
                var label = _enemyLabels.Next();
                Place(label, panel, cam, e.Position + Vector3.up * 2.3f);
                label.text = e.Windup ? $"! {e.Eta:0.0}" : e.Staggered ? "よろけ" : $"{e.Eta:0.0}";
                label.EnableInClassList("world-label--windup", e.Windup);
                label.EnableInClassList("world-label--soon", !e.Windup && e.Eta < 1.2f);
            }
            _enemyLabels.End();

            _ghostLabels.Begin();
            foreach (var g in s.Ghosts)
            {
                var label = _ghostLabels.Next();
                Place(label, panel, cam, g.Position + Vector3.up * 2.4f);
                label.text = (g.Order + 1).ToString(CultureInfo.InvariantCulture);
            }
            _ghostLabels.End();

            // 足元のフィルム帯: スロー中は「今解除したら」の硬直、硬直中は残り
            float total = 0f, remaining = 0f;
            if (slow && s.Placed > 0) total = remaining = s.CommitIfReleased;
            else if (s.CutPhase == CutPhase.Commit) { total = s.CommitDuration; remaining = s.CommitRemaining; }
            bool show = total > 0f;
            _filmStrip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            int frames = Mathf.Max(1, Mathf.RoundToInt(total / FrameSeconds));
            int alive = Mathf.Clamp(Mathf.CeilToInt(remaining / FrameSeconds), 0, frames);
            while (_filmFrames.Count < frames)
            {
                var f = new VisualElement { pickingMode = PickingMode.Ignore };
                f.AddToClassList("film-strip__frame");
                _filmStrip.Insert(_filmFrames.Count, f);
                _filmFrames.Add(f);
            }
            for (int i = 0; i < _filmFrames.Count; i++)
            {
                _filmFrames[i].style.display = i < frames ? DisplayStyle.Flex : DisplayStyle.None;
                _filmFrames[i].EnableInClassList("film-strip__frame--spent", i >= alive);
            }
            _filmLabel.text = $"硬直 {remaining:0.0}s";
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, s.PlayerPosition + Vector3.down * 0.15f, cam);
            _filmStrip.style.left = p.x;
            _filmStrip.style.top = p.y;
        }

        void RenderFeedback(HudSnapshot s, Camera cam, float dt)
        {
            var panel = _root.panel;
            foreach (var e in s.Feedback)
            {
                switch (e.Kind)
                {
                    case FeedbackKind.Hit:
                        SpawnPopup(e.Position, $"{e.Value:0}", null);
                        break;
                    case FeedbackKind.CutHit:
                        SpawnPopup(e.Position, e.Extra > 0 ? $"{e.Value:0}  映え+{e.Extra}" : $"{e.Value:0}", "world-label--popup-cut");
                        break;
                    case FeedbackKind.Parry:
                        SpawnPopup(e.Position, "捌き!", "world-label--popup-parry");
                        break;
                    case FeedbackKind.PlayerHurt:
                        SpawnPopup(e.Position, e.Extra == 1 ? $"-{e.Value:0} 硬直中!" : $"-{e.Value:0}", "world-label--popup-hurt");
                        break;
                    case FeedbackKind.PoseRejected:
                        SpawnPopup(e.Position, RejectText((PlacementResult)e.Extra), "world-label--popup-hurt");
                        break;
                    case FeedbackKind.SlowEnter:
                        ShowBanner("止める", "", null, sticky: false, life: 0.5f);
                        break;
                    case FeedbackKind.CutRelease:
                        ShowBanner("切る!", $"{e.Extra} ポーズ", null, sticky: false, life: 0.5f);
                        break;
                    case FeedbackKind.NG:
                        ShowBanner("NG!", NgText(s), "banner__text--ng", sticky: true);
                        break;
                    case FeedbackKind.Clear:
                        ShowBanner("カット! OK", $"テイク {e.Extra} で撮り切り", "banner__text--ok", sticky: true);
                        break;
                    case FeedbackKind.TakeStart:
                        ShowBanner($"テイク {e.Extra}", s.CommitEnabled ? "コミット時間 あり" : "コミット時間 なし", null, sticky: false);
                        break;
                }
            }

            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                p.Age += dt;
                if (p.Age >= PopupLife || cam == null || panel == null)
                {
                    p.Label.RemoveFromHierarchy();
                    _popups.RemoveAt(i);
                    continue;
                }
                float k = p.Age / PopupLife;
                var pos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, p.World + Vector3.up * (2.0f + k * 0.8f), cam);
                p.Label.style.left = pos.x;
                p.Label.style.top = pos.y;
                p.Label.style.opacity = 1f - k * k;
            }
        }

        void SpawnPopup(Vector3 world, string text, string extraClass)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("world-label");
            label.AddToClassList("world-label--popup");
            if (extraClass != null) label.AddToClassList(extraClass);
            _world.Add(label);
            _popups.Add(new Popup { Label = label, World = world });
        }

        void ShowBanner(string text, string sub, string extraClass, bool sticky, float life = BannerLife)
        {
            _bannerText.text = text;
            _bannerText.EnableInClassList("banner__text--ng", extraClass == "banner__text--ng");
            _bannerText.EnableInClassList("banner__text--ok", extraClass == "banner__text--ok");
            _bannerSub.text = sub;
            _bannerSub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            _bannerTimer = life;
            _bannerSticky = sticky;
            _banner.AddToClassList("banner--visible");
        }

        void RenderBanner(HudSnapshot s, float dt)
        {
            if (_bannerSticky && (s.Phase == SessionPhase.NG || s.Phase == SessionPhase.Cleared)) return;
            _bannerSticky = false;
            _bannerTimer -= dt;
            if (_bannerTimer <= 0f) _banner.RemoveFromClassList("banner--visible");
        }

        static string NgText(HudSnapshot s)
        {
            string why = s.LastNgReason switch
            {
                NgReason.HitDuringCommit => $"硬直中に殴られた ({s.LastCutPoses} ポーズ / 硬直 {s.LastCommitTime:0.0}s)",
                NgReason.HitWhileFree => "通常の攻防で削られた",
                _ => "",
            };
            return string.IsNullOrEmpty(why) ? "テイク2へ" : why;
        }

        static string RejectText(PlacementResult r) => r switch
        {
            PlacementResult.NotEnoughGauge => "フィルム不足",
            PlacementResult.RequiresTable => "机の上にだけ置ける",
            PlacementResult.TooManyPoses => "これ以上置けない",
            _ => "置けない",
        };

        void RenderStats(PlaytestStats stats, string logPath)
        {
            _stats.EnableInClassList("stats--visible", stats != null);
            if (stats == null) return;
            _statsGrid ??= BuildStatsGrid();

            // 行: 条件(硬直あり/なし) × 列: 包囲人数。値は「平均ポーズ数 (サンプル数)」
            for (int row = 0; row < 2; row++)
            {
                bool commit = row == 0;
                for (int b = 0; b < PlaytestStats.Buckets; b++)
                {
                    _statsGrid[row, b].text = stats.TryAveragePoses(commit, b, out float avg)
                        ? $"{avg:0.00} ({stats.CutCount(commit, b)})"
                        : "—";
                }
            }

            _sb.Clear();
            _sb.Append($"スロー使用: あり {stats.SlowUses(true)} / なし {stats.SlowUses(false)}\n");
            _sb.Append($"NG: あり {stats.NgCount(true)} / なし {stats.NgCount(false)}\n");
            _sb.Append($"硬直中の被弾: {stats.HitsDuringCommit(true)}");
            if (!string.IsNullOrEmpty(logPath)) _sb.Append($"\n記録: {logPath}");
            _statsBody.text = _sb.ToString();
        }

        Label[,] _statsGrid;

        Label[,] BuildStatsGrid()
        {
            var grid = new Label[2, PlaytestStats.Buckets];
            var table = new VisualElement { pickingMode = PickingMode.Ignore };
            table.AddToClassList("stats__table");
            table.Add(StatsRow("平均ポーズ数", new[] { "敵1", "敵2", "敵3+" }, null, 0));
            table.Add(StatsRow("硬直あり", null, grid, 0));
            table.Add(StatsRow("硬直なし", null, grid, 1));
            _stats.Insert(1, table);
            return grid;
        }

        static VisualElement StatsRow(string head, string[] fixedCells, Label[,] grid, int gridRow)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("stats__row");
            var h = new Label(head);
            h.AddToClassList("stats__cell");
            h.AddToClassList("stats__cell--head");
            row.Add(h);
            for (int b = 0; b < PlaytestStats.Buckets; b++)
            {
                var cell = new Label(fixedCells != null ? fixedCells[b] : "—");
                cell.AddToClassList("stats__cell");
                if (fixedCells != null) cell.AddToClassList("stats__cell--caption");
                else grid[gridRow, b] = cell;
                row.Add(cell);
            }
            return row;
        }

        static void Place(VisualElement e, IPanel panel, Camera cam, Vector3 world)
        {
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
            e.style.left = p.x;
            e.style.top = p.y;
        }

        static void SetWidth(VisualElement e, float t01) => e.style.width = Length.Percent(100f * Mathf.Clamp01(t01));

        /// <summary>
        /// 日本語のグリフを出すため、OS の日本語フォントから動的フォントを作る。
        /// フォントファイルを同梱しない(再配布条件を気にせず済む)。見つからなければ既定のまま。
        /// </summary>
        static void ApplyJapaneseFont(VisualElement root)
        {
            foreach (var family in new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "BIZ UDGothic", "MS Gothic", "Hiragino Sans", "Noto Sans CJK JP" })
            {
                try
                {
                    var font = FontAsset.CreateFontAsset(family, "Regular");
                    if (font == null) font = FontAsset.CreateFontAsset(family, "Bold");
                    if (font == null) continue;
                    root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
                    return;
                }
                catch (System.Exception)
                {
                    // 次の候補へ
                }
            }
        }

        /// <summary>毎フレーム同じ数だけ使うラベルの使い回し。</summary>
        sealed class LabelPool
        {
            readonly VisualElement _parent;
            readonly string[] _classes;
            readonly List<Label> _items = new();
            int _used;

            public LabelPool(VisualElement parent, params string[] classes)
            {
                _parent = parent;
                _classes = classes;
            }

            public void Begin() => _used = 0;

            public Label Next()
            {
                if (_used == _items.Count)
                {
                    var l = new Label { pickingMode = PickingMode.Ignore };
                    foreach (var c in _classes) l.AddToClassList(c);
                    _parent.Add(l);
                    _items.Add(l);
                }
                var label = _items[_used++];
                label.style.display = DisplayStyle.Flex;
                return label;
            }

            public void End()
            {
                for (int i = _used; i < _items.Count; i++) _items[i].style.display = DisplayStyle.None;
            }
        }
    }
}
