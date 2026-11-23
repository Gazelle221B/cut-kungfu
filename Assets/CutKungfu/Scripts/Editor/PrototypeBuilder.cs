using System.Collections.Generic;
using System.IO;
using CutKungfu.Authoring;
using CutKungfu.Core;
using CutKungfu.Gameplay;
using CutKungfu.Presentation;
using Unity.Rendering;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CutKungfu.EditorTools
{
    /// <summary>
    /// グレーボックスの試作シーン一式(マテリアル・プレハブ・サブシーン・メインシーン・UI 設定・調整値 JSON)を
    /// コードから組み立てる。手作業の配線を残さないので、何度でも同じ物が作り直せる。
    /// メニュー: Cut Kungfu/Build Prototype Scene
    /// </summary>
    public static class PrototypeBuilder
    {
        const string Root = "Assets/CutKungfu";
        const string MatDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir = Root + "/Scenes";
        const string UiDir = Root + "/UI";
        public const string MainScenePath = SceneDir + "/Prototype.unity";
        public const string StageScenePath = SceneDir + "/PrototypeStage.unity";

        // 1970年代カンフー映画のパレット(U1 コンセプトより): 赤は主人公だけ、水色はゴーストだけ
        static readonly Color Tomato = Hex("#D9452B");
        static readonly Color Mustard = Hex("#E0A82E");
        static readonly Color Cream = Hex("#EADBB8");
        static readonly Color GreyTeal = Hex("#6E8C8A");
        static readonly Color DustyTeal = Hex("#4A6E70");
        static readonly Color Ink = Hex("#2A2420");
        static readonly Color Wood = Hex("#8A5A36");
        static readonly Color Crate = Hex("#A57A45");
        static readonly Color GhostCyan = new(0.66f, 0.94f, 1f, 0.45f);

        [MenuItem("Cut Kungfu/Build Prototype Scene", priority = 0)]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        [MenuItem("Cut Kungfu/Open Tuning JSON", priority = 20)]
        public static void OpenTuning()
        {
            EnsureTuningJson();
            EditorUtility.OpenWithDefaultApp(TuningLoaderSystem.FilePath);
        }

        [MenuItem("Cut Kungfu/Reveal Playtest Logs", priority = 21)]
        public static void RevealLogs()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Playtests");
            Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        public static void Build()
        {
            foreach (var d in new[] { MatDir, PrefabDir, SceneDir, UiDir }) EnsureFolder(d);
            EnsureTuningJson();

            var mats = new Materials
            {
                Hero = LitMaterial("M_Hero", Tomato),
                Sash = LitMaterial("M_Sash", Mustard),
                Enemy = LitMaterial("M_Extra", GreyTeal),
                Ink = LitMaterial("M_Ink", Ink),
                Floor = LitMaterial("M_Floor", Cream),
                Wall = LitMaterial("M_Wall", DustyTeal),
                Table = LitMaterial("M_Table", Wood),
                Crate = LitMaterial("M_Crate", Crate),
                Ghost = TransparentUnlit("M_Ghost", GhostCyan),
                StrikeFx = TransparentUnlit("M_StrikeFx", new Color(1f, 1f, 1f, 0.5f)),
            };

            var prefabs = BuildPrefabs(mats);
            BuildStageScene(mats, prefabs);
            BuildMainScene();
            AddToBuildSettings(MainScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CutKungfu] 試作シーンを組み立てました: " + MainScenePath);
        }

        // ------------------------------------------------------------------ prefabs

        sealed class Materials
        {
            public Material Hero, Sash, Enemy, Ink, Floor, Wall, Table, Crate, Ghost, StrikeFx;
        }

        sealed class Prefabs
        {
            public GameObject Hero, Enemy, StrikeFx;
            public readonly Dictionary<BrickKind, GameObject> Ghosts = new();
        }

        static Prefabs BuildPrefabs(Materials m)
        {
            var p = new Prefabs
            {
                Hero = SavePrefab(Character("Hero", Team.Player, m.Hero, m.Ink, m.Sash, Tomato), "Hero"),
                Enemy = SavePrefab(Character("Extra", Team.Enemy, m.Enemy, m.Ink, null, GreyTeal), "Extra"),
            };

            for (int i = 0; i < BrickKinds.Count; i++)
            {
                var kind = (BrickKind)i;
                p.Ghosts[kind] = SavePrefab(Ghost(kind, m.Ghost), "Ghost_" + kind);
            }

            var fx = Primitive(PrimitiveType.Cylinder, "StrikeFx", null, Vector3.zero, new Vector3(1f, 0.01f, 1f), m.StrikeFx);
            fx.AddComponent<StrikeFxAuthoring>();
            fx.AddComponent<URPMaterialPropertyBaseColorAuthoring>().color = Color.white;
            p.StrikeFx = SavePrefab(fx, "StrikeFx");
            return p;
        }

        static GameObject Character(string name, Team team, Material body, Material ink, Material sash, Color baseColor)
        {
            var root = new GameObject(name);
            var bodyGo = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 1f, 0), new Vector3(0.8f, 1f, 0.8f), body);
            bodyGo.AddComponent<URPMaterialPropertyBaseColorAuthoring>().color = baseColor;
            // 向きが一目で分かる「鼻」(主人公は鼻の絆創膏の位置)
            Primitive(PrimitiveType.Cube, "Nose", root.transform, new Vector3(0, 1.55f, 0.38f), new Vector3(0.26f, 0.16f, 0.3f), ink);
            if (sash != null)
                Primitive(PrimitiveType.Cylinder, "Sash", root.transform, new Vector3(0, 0.95f, 0), new Vector3(0.84f, 0.07f, 0.84f), sash);

            var fa = root.AddComponent<FighterAuthoring>();
            fa.team = team;
            fa.body = bodyGo;
            fa.baseColor = baseColor;
            return root;
        }

        /// <summary>決めポーズの人影。シルエットで種類が分かるように、胴と手足の向きを変える。</summary>
        static GameObject Ghost(BrickKind kind, Material mat)
        {
            var root = new GameObject("Ghost_" + kind);
            GameObject body;
            switch (kind)
            {
                case BrickKind.Palm: // 踏み込んで掌を突き出す
                    body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 1f, 0), new Vector3(0.8f, 1f, 0.8f), mat, new Vector3(12, 0, 0));
                    Primitive(PrimitiveType.Cube, "Arm", root.transform, new Vector3(0.15f, 1.35f, 0.7f), new Vector3(0.18f, 0.18f, 1.0f), mat);
                    Primitive(PrimitiveType.Cube, "Palm", root.transform, new Vector3(0.15f, 1.35f, 1.22f), new Vector3(0.32f, 0.36f, 0.08f), mat);
                    break;
                case BrickKind.Spin: // 片脚を水平に払う
                    body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 1f, 0), new Vector3(0.8f, 1f, 0.8f), mat, new Vector3(0, 0, -14));
                    Primitive(PrimitiveType.Cube, "Leg", root.transform, new Vector3(0.75f, 1.1f, 0.2f), new Vector3(1.4f, 0.2f, 0.2f), mat, new Vector3(0, 30, 0));
                    break;
                case BrickKind.FlyingKick: // 空中で前へ脚を伸ばす
                    body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 1.7f, -0.1f), new Vector3(0.7f, 0.9f, 0.7f), mat, new Vector3(55, 0, 0));
                    Primitive(PrimitiveType.Cube, "Leg", root.transform, new Vector3(0, 1.5f, 0.85f), new Vector3(0.22f, 0.22f, 1.1f), mat, new Vector3(-10, 0, 0));
                    break;
                default: // 机上の着地: 低く構えて両腕を開く
                    body = Primitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 0.7f, 0), new Vector3(0.85f, 0.7f, 0.85f), mat);
                    Primitive(PrimitiveType.Cube, "Arms", root.transform, new Vector3(0, 1.05f, 0), new Vector3(1.9f, 0.16f, 0.16f), mat);
                    break;
            }
            body.AddComponent<URPMaterialPropertyBaseColorAuthoring>().color = GhostCyan;
            var ga = root.AddComponent<GhostAuthoring>();
            ga.kind = kind;
            ga.body = body;
            return root;
        }

        // ------------------------------------------------------------------ scenes

        static void BuildStageScene(Materials m, Prefabs p)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 撮影所の裏通り: 左に木箱、右寄りに屋台の机、奥に壁(U1 のロケーション)
            var set = new GameObject("Set").transform;
            Primitive(PrimitiveType.Cube, "Floor", set, new Vector3(0, -0.1f, 0), new Vector3(26f, 0.2f, 10f), m.Floor);
            Primitive(PrimitiveType.Cube, "BackWall", set, new Vector3(0, 2f, 4.6f), new Vector3(26f, 4f, 0.4f), m.Wall);
            Primitive(PrimitiveType.Cube, "LeftWall", set, new Vector3(-12.7f, 2f, 0), new Vector3(0.4f, 4f, 10f), m.Wall);
            Primitive(PrimitiveType.Cube, "RightWall", set, new Vector3(12.7f, 2f, 0), new Vector3(0.4f, 4f, 10f), m.Wall);
            var crates = new[] { new Vector3(-11.4f, 0.45f, 3.6f), new Vector3(-10.4f, 0.45f, 3.7f), new Vector3(-10.9f, 1.35f, 3.6f), new Vector3(-11.5f, 0.45f, 2.6f) };
            for (int i = 0; i < crates.Length; i++)
                Primitive(PrimitiveType.Cube, "Crate" + i, set, crates[i], Vector3.one * 0.9f, m.Crate, new Vector3(0, i * 7f, 0));
            for (int i = 0; i < 4; i++)
                Primitive(PrimitiveType.Cylinder, "Lamp" + i, set, new Vector3(-9f + i * 6f, 5.6f, 3.4f), new Vector3(0.5f, 0.25f, 0.5f), m.Ink, new Vector3(25, 0, 0));

            var table = Primitive(PrimitiveType.Cube, "MarketTable", null, new Vector3(3f, 0.4f, 0.8f), new Vector3(2.4f, 0.8f, 1.2f), m.Table);
            table.AddComponent<TableAuthoring>();

            var arena = new GameObject("Arena").AddComponent<ArenaAuthoring>();
            arena.min = new Vector2(-12.2f, -4f);
            arena.max = new Vector2(12.2f, 4.1f);

            var hero = (GameObject)PrefabUtility.InstantiatePrefab(p.Hero, scene);
            hero.transform.position = new Vector3(-9.5f, 0f, 0f);

            var scenario = new GameObject("Scenario").AddComponent<ScenarioAuthoring>();
            scenario.enemy = p.Enemy;
            scenario.ghostPalm = p.Ghosts[BrickKind.Palm];
            scenario.ghostSpin = p.Ghosts[BrickKind.Spin];
            scenario.ghostFlyingKick = p.Ghosts[BrickKind.FlyingKick];
            scenario.ghostTableLanding = p.Ghosts[BrickKind.TableLanding];
            scenario.strikeFx = p.StrikeFx;
            // GDD 6章の2盤面: A=敵1体・背後は壁 / B=敵3体の包囲(U1 の配置: 左・机の手前右・机の奥右)
            scenario.waves = new List<ScenarioAuthoring.Wave>
            {
                new()
                {
                    name = "A: 敵1体・背後は壁",
                    playerStart = new Vector3(-9.5f, 0, 0),
                    enemySpawns = new List<Vector3> { new(-4.5f, 0, 0.5f) },
                },
                new()
                {
                    name = "B: 敵3体の包囲",
                    playerStart = new Vector3(0f, 0, -0.5f),
                    enemySpawns = new List<Vector3> { new(-3.2f, 0, -0.3f), new(2.6f, 0, -1.9f), new(5.2f, 0, 2.3f) },
                },
            };

            EditorSceneManager.SaveScene(scene, StageScenePath);
        }

        static void BuildMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 48f;
            cam.nearClipPlane = 0.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#2B2622");
            camGo.transform.SetPositionAndRotation(new Vector3(-6f, 5.4f, -10.5f), Quaternion.Euler(20f, 0f, 0f));
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            var lightGo = new GameObject("Key Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("#FFE9C4");
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Hex("#7A6E62");

            var volumeGo = new GameObject("Slow Film Look (Volume)");
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.weight = 0f;
            volume.priority = 10f;
            volume.sharedProfile = SlowProfile();

            var hudGo = new GameObject("Presentation");
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = PanelSettingsAsset();
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiDir + "/Hud.uxml");
            var audio = hudGo.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            var root = hudGo.AddComponent<PresentationRoot>();
            var so = new SerializedObject(root);
            so.FindProperty("sceneCamera").objectReferenceValue = cam;
            so.FindProperty("hud").objectReferenceValue = doc;
            so.FindProperty("slowVolume").objectReferenceValue = volume;
            so.FindProperty("sfxSource").objectReferenceValue = audio;
            so.ApplyModifiedPropertiesWithoutUndo();

            var subGo = new GameObject("Stage (SubScene)");
            var sub = subGo.AddComponent<SubScene>();
            sub.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(StageScenePath);
            sub.AutoLoadScene = true;

            EditorSceneManager.SaveScene(scene, MainScenePath);
        }

        static VolumeProfile SlowProfile()
        {
            string path = Root + "/Settings/SlowFilmLook.asset";
            EnsureFolder(Root + "/Settings");
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var c in profile.components.ToArray())
            {
                profile.Remove(c.GetType());
                Object.DestroyImmediate(c, true);
            }

            // セピアに褪せ、粒子と周辺減光を足す(U1 Shot 2 の「色が抜けて時間が這う」)
            var color = profile.Add<ColorAdjustments>(true);
            color.saturation.Override(-75f);
            color.colorFilter.Override(new Color(1f, 0.86f, 0.66f));
            color.contrast.Override(12f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.5f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.45f);
            var ca = profile.Add<ChromaticAberration>(true);
            ca.intensity.Override(0.25f);
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static PanelSettings PanelSettingsAsset()
        {
            string themePath = UiDir + "/CutKungfuTheme.tss";
            if (!File.Exists(themePath))
            {
                File.WriteAllText(themePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(themePath);
            }
            string path = UiDir + "/HudPanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ------------------------------------------------------------------ helpers

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // 物理エンジンは使わない(判定は Gameplay 層の XZ 平面計算)。焼き込みで物理ボディを作らせない
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = mat != null && mat.renderQueue >= 3000 ? ShadowCastingMode.Off : ShadowCastingMode.On;
            return go;
        }

        static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Material LitMaterial(string name, Color color)
        {
            var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material TransparentUnlit(string name, Color color)
        {
            var m = LoadOrCreateMaterial(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreateMaterial(string name, string shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader)) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader.name != shader)
            {
                m.shader = Shader.Find(shader);
            }
            return m;
        }

        static void EnsureTuningJson()
        {
            string path = TuningLoaderSystem.FilePath;
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, TuningLoaderSystem.ToJson(Tuning.Default));
            AssetDatabase.Refresh();
        }

        static void AddToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == scenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
    }
}
