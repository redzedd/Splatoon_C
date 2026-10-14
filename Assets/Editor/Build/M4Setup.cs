using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Player;
using SplatoonC.Gameplay.Scoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.EditorBuild
{
    // M4(對戰要素)的接線腳本:圖層、輸入動作引用、資料資產、材質、prefab、Player prefab、場景。
    // 冪等、可重跑;任一查找失敗就 LogError 中止,不留半接線狀態。
    // 選單 Tools/SplatoonC/M4 Setup,或 batchmode -executeMethod SplatoonC.EditorBuild.M4Setup.Apply。
    // 注意:絕不呼叫 M2Setup.Apply(它會把 WeaponConfig 寫回過期數值)。
    public static class M4Setup
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string DummyConfigPath = "Assets/Data/TargetDummyConfig.asset";
        private const string BombConfigPath = "Assets/Data/SplatBombConfig.asset";
        private const string InkstrikeConfigPath = "Assets/Data/TripleInkstrikeConfig.asset";
        private const string DummyMaterialPath = "Assets/Materials/TargetDummy.mat";
        private const string BombMaterialPath = "Assets/Materials/SplatBomb.mat";
        private const string ArcMaterialPath = "Assets/Materials/ThrowArc.mat";
        private const string DeviceMaterialPath = "Assets/Materials/InkstrikeDevice.mat";
        private const string TornadoMaterialPath = "Assets/Materials/Tornado.mat";
        private const string DummyPrefabPath = "Assets/Prefabs/TargetDummy.prefab";
        private const string BombPrefabPath = "Assets/Prefabs/SplatBomb.prefab";
        private const string DevicePrefabPath = "Assets/Prefabs/InkstrikeDevice.prefab";
        private const string TornadoPrefabPath = "Assets/Prefabs/Tornado.prefab";
        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string UrpUnlit = "Universal Render Pipeline/Unlit";

        // 假人圖層槽(TagManager 第 6 槽,原本空著);設定碼用字面值,不靠 NameToLayer 的時序
        public const int TargetLayer = 6;
        private const int PlayerLayer = 8;

        private static readonly Color InkOrange = new Color(1f, 0.5f, 0f, 1f);

        // 靶場全放 x ≥ 17 的帶狀區:現有 AutoTest/Probe 的射道都在 x ≤ 10.5 朝 -Z,互不污染
        private static readonly Vector3 TargetWallPosition = new Vector3(24.5f, 1.5f, 6f);
        private static readonly Vector3 TargetWallScale = new Vector3(1f, 3f, 6f);
        private static readonly Vector3 DummyNearPosition = new Vector3(21f, 0f, -10f);
        private static readonly Vector3 DummyFarPosition = new Vector3(24f, 0f, -2f);
        private static readonly Vector3 DummyMovingPosition = new Vector3(18f, 0f, 6f);

        [MenuItem("Tools/SplatoonC/M4 Setup")]
        public static void Apply()
        {
            if (Application.isPlaying)
            {
                Fail("Play mode 中不可接線");
                return;
            }
            if (!EnsureTargetLayer() || !EnsureInputReferences() || !EnsureDataAssets()
                || !EnsureMaterials() || !EnsurePrefabs() || !EnsurePlayerPrefab() || !EnsureScene())
            {
                return;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[M4SETUP] 完成");
        }

        private static void Fail(string message)
        {
            Debug.LogError("[M4SETUP] ERROR " + message);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        // 步驟 1:TagManager layers[6] = "Target"。
        private static bool EnsureTargetLayer()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Fail("找不到 ProjectSettings/TagManager.asset");
                return false;
            }
            var so = new SerializedObject(assets[0]);
            SerializedProperty slot = so.FindProperty("layers").GetArrayElementAtIndex(TargetLayer);
            if (slot.stringValue == "Target")
            {
                return true;
            }
            if (!string.IsNullOrEmpty(slot.stringValue))
            {
                Fail($"圖層槽 {TargetLayer} 已被「{slot.stringValue}」占用,無法建立 Target 圖層");
                return false;
            }
            slot.stringValue = "Target";
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[M4SETUP] 圖層 Target = {TargetLayer}");
            return true;
        }

        // 步驟 2:同步 reimport 動作表 → 找 Player/SubWeapon、Player/Special 的 InputActionReference 子資產
        // (子資產 fileID 由匯入器從動作 GUID 雜湊而來,無法手寫進 prefab YAML)→ 接到 Player prefab。
        private static bool EnsureInputReferences()
        {
            AssetDatabase.ImportAsset(InputActionsPath,
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            InputActionReference sub = FindActionReference("Player", "SubWeapon");
            InputActionReference special = FindActionReference("Player", "Special");
            if (sub == null || special == null)
            {
                Fail("動作表缺少 Player/SubWeapon 或 Player/Special(先確認 .inputactions 已加入這兩個動作)");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var router = root.GetComponent<PlayerInputRouter>();
                if (router == null)
                {
                    Fail("Player prefab 缺少 PlayerInputRouter");
                    return false;
                }
                var so = new SerializedObject(router);
                bool changed = false;
                if (!TrySetReference(so, "_subWeapon", sub, ref changed)
                    || !TrySetReference(so, "_special", special, ref changed))
                {
                    return false;
                }
                if (changed)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[M4SETUP] Player prefab 接上 SubWeapon/Special 動作引用");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return true;
        }

        private static InputActionReference FindActionReference(string mapName, string actionName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(InputActionsPath))
            {
                var reference = asset as InputActionReference;
                if (reference == null || reference.action == null || reference.action.actionMap == null)
                {
                    continue;
                }
                if (reference.action.actionMap.name == mapName && reference.action.name == actionName)
                {
                    return reference;
                }
            }
            return null;
        }

        // 用 SerializedObject 設 [SerializeField] private 欄位;欄位不存在 → Fail(腳本未編譯或改名)。
        private static bool TrySetReference(SerializedObject so, string field, Object value, ref bool changed)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                Fail($"{so.targetObject.GetType().Name} 缺少欄位 {field}(腳本尚未編譯?)");
                return false;
            }
            if (property.objectReferenceValue != value)
            {
                property.objectReferenceValue = value;
                changed = true;
            }
            return true;
        }

        // 步驟 3:資料資產(缺才建,用 code 預設值;之後調參一律改 .asset)。
        private static bool EnsureDataAssets()
        {
            if (EnsureAsset<TargetDummyConfig>(DummyConfigPath, out _) == null
                || EnsureAsset<SplatBombConfig>(BombConfigPath, out _) == null)
            {
                return false;
            }
            var inkstrike = EnsureAsset<TripleInkstrikeConfig>(InkstrikeConfigPath, out bool created);
            if (inkstrike == null)
            {
                return false;
            }
            if (created)
            {
                // 裝置要比炸彈丟得遠:基底預設是炸彈的 14 / 20°,新建時改成 22 / 25°(之後調參一律改 .asset)
                var so = new SerializedObject(inkstrike);
                so.FindProperty("_throwSpeed").floatValue = 22f;
                so.FindProperty("_liftAngleDeg").floatValue = 25f;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(inkstrike);
            }
            return true;
        }

        private static T EnsureAsset<T>(string path, out bool created) where T : ScriptableObject
        {
            created = false;
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            Debug.Log($"[M4SETUP] 建立 {path}");
            return asset;
        }

        // 步驟 4:材質(缺才建)。材質資產本身引用 shader,不會被 build 剔除。
        private static bool EnsureMaterials()
        {
            return EnsureMaterial(DummyMaterialPath, UrpLit, new Color(0.85f, 0.85f, 0.8f, 1f), false) != null
                && EnsureMaterial(BombMaterialPath, UrpUnlit, new Color(0.12f, 0.12f, 0.16f, 1f), false) != null
                && EnsureMaterial(ArcMaterialPath, UrpUnlit, new Color(InkOrange.r, InkOrange.g, InkOrange.b, 0.85f), true) != null
                && EnsureMaterial(DeviceMaterialPath, UrpUnlit, InkOrange, false) != null
                && EnsureMaterial(TornadoMaterialPath, UrpUnlit, new Color(InkOrange.r, InkOrange.g, InkOrange.b, 0.55f), true) != null;
        }

        private static Material EnsureMaterial(string path, string shaderName, Color baseColor, bool transparent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Fail($"找不到 shader {shaderName}");
                return null;
            }
            var material = new Material(shader);
            material.SetColor("_BaseColor", baseColor);
            if (transparent)
            {
                ConfigureTransparent(material);
            }
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"[M4SETUP] 建立 {path}");
            return material;
        }

        // URP 材質從程式建立預設 Opaque;半透明要補齊這些欄位,否則畫成實心方塊(已知的坑)。
        private static void ConfigureTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        // 步驟 5:prefab(缺才建)。
        private static bool EnsurePrefabs()
        {
            var dummyConfig = AssetDatabase.LoadAssetAtPath<TargetDummyConfig>(DummyConfigPath);
            var dummyMaterial = AssetDatabase.LoadAssetAtPath<Material>(DummyMaterialPath);
            var bombMaterial = AssetDatabase.LoadAssetAtPath<Material>(BombMaterialPath);
            var deviceMaterial = AssetDatabase.LoadAssetAtPath<Material>(DeviceMaterialPath);
            var tornadoMaterial = AssetDatabase.LoadAssetAtPath<Material>(TornadoMaterialPath);
            if (dummyConfig == null || dummyMaterial == null || bombMaterial == null
                || deviceMaterial == null || tornadoMaterial == null)
            {
                Fail("假人設定或材質不存在,無法建 prefab");
                return false;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath) == null)
            {
                BuildDummyPrefab(dummyConfig, dummyMaterial);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath) == null)
            {
                BuildBombPrefab(bombMaterial);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DevicePrefabPath) == null)
            {
                BuildDevicePrefab(deviceMaterial);
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(TornadoPrefabPath) == null)
            {
                BuildTornadoPrefab(tornadoMaterial);
            }
            return true;
        }

        private static void BuildDevicePrefab(Material material)
        {
            GameObject root = CreateVisual(PrimitiveType.Cube, "InkstrikeDevice", null, material, 0);
            try
            {
                root.transform.localScale = Vector3.one * 0.35f;
                root.AddComponent<InkstrikeDevice>();
                PrefabUtility.SaveAsPrefabAsset(root, DevicePrefabPath);
                Debug.Log($"[M4SETUP] 建立 {DevicePrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 龍捲風:空根(Tornado 元件)+ 子物件 Pillar(Cylinder,半透明雙面 Unlit);縮放與位置由執行期每幀設定。
        private static void BuildTornadoPrefab(Material material)
        {
            var root = new GameObject("Tornado");
            try
            {
                GameObject pillar = CreateVisual(PrimitiveType.Cylinder, "Pillar", root.transform, material, 0);
                var tornado = root.AddComponent<Tornado>();
                var so = new SerializedObject(tornado);
                so.FindProperty("_pillar").objectReferenceValue = pillar.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, TornadoPrefabPath);
                Debug.Log($"[M4SETUP] 建立 {TornadoPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildDummyPrefab(TargetDummyConfig config, Material material)
        {
            var root = new GameObject("TargetDummy");
            try
            {
                root.layer = TargetLayer;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 1f, 0f);
                capsule.height = 2f;
                capsule.radius = 0.4f;

                GameObject body = CreateVisual(PrimitiveType.Capsule, "Body", root.transform, material, TargetLayer);
                body.transform.localPosition = new Vector3(0f, 1f, 0f);

                var anchor = new GameObject("PopupAnchor");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.localPosition = new Vector3(0f, 2.4f, 0f);

                var dummy = root.AddComponent<TargetDummy>();
                var so = new SerializedObject(dummy);
                so.FindProperty("_config").objectReferenceValue = config;
                so.FindProperty("_popupAnchor").objectReferenceValue = anchor.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, DummyPrefabPath);
                Debug.Log($"[M4SETUP] 建立 {DummyPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildBombPrefab(Material material)
        {
            GameObject root = CreateVisual(PrimitiveType.Sphere, "SplatBomb", null, material, 0);
            try
            {
                root.transform.localScale = Vector3.one * 0.5f;
                root.AddComponent<SplatBomb>();
                PrefabUtility.SaveAsPrefabAsset(root, BombPrefabPath);
                Debug.Log($"[M4SETUP] 建立 {BombPrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 純視覺 primitive:拆掉自帶 collider(全專案碰撞只靠射線,多餘 collider 會被墨彈打到)
        private static GameObject CreateVisual(PrimitiveType type, string name, Transform parent, Material material, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = layer;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        // 步驟 6:Player prefab——ThrowController + ThrowArc(LineRenderer)+ ThrowMarker,接線。
        // 存檔後同步 Refresh:步驟 7 要讀場景裡 Player 實例上的新元件。
        private static bool EnsurePlayerPrefab()
        {
            var bombConfig = AssetDatabase.LoadAssetAtPath<SplatBombConfig>(BombConfigPath);
            var inkstrikeConfig = AssetDatabase.LoadAssetAtPath<TripleInkstrikeConfig>(InkstrikeConfigPath);
            var bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
            var devicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DevicePrefabPath);
            var tornadoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TornadoPrefabPath);
            var arcMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArcMaterialPath);
            if (bombConfig == null || inkstrikeConfig == null || bombPrefab == null || devicePrefab == null
                || tornadoPrefab == null || arcMaterial == null)
            {
                Fail("炸彈/大招設定、炸彈/裝置/龍捲風 prefab 或 ThrowArc 材質不存在,無法接 Player prefab");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Transform muzzle = FindDeep(root.transform, "Muzzle");
                if (muzzle == null)
                {
                    Fail("Player prefab 找不到 Muzzle");
                    return false;
                }
                bool changed = false;
                var thrower = root.GetComponent<ThrowController>();
                if (thrower == null)
                {
                    thrower = root.AddComponent<ThrowController>();
                    changed = true;
                }

                Transform arc = root.transform.Find("ThrowArc");
                if (arc == null)
                {
                    var arcGo = new GameObject("ThrowArc", typeof(LineRenderer));
                    arcGo.layer = PlayerLayer;
                    arc = arcGo.transform;
                    arc.SetParent(root.transform, false);
                    var line = arcGo.GetComponent<LineRenderer>();
                    line.useWorldSpace = true;
                    line.startWidth = 0.08f;
                    line.endWidth = 0.03f;
                    line.numCapVertices = 2;
                    line.numCornerVertices = 2;
                    line.sharedMaterial = arcMaterial;
                    line.shadowCastingMode = ShadowCastingMode.Off;
                    line.receiveShadows = false;
                    line.positionCount = 0;
                    line.enabled = false;
                    changed = true;
                }

                Transform marker = root.transform.Find("ThrowMarker");
                if (marker == null)
                {
                    GameObject markerGo = CreateVisual(PrimitiveType.Cylinder, "ThrowMarker", root.transform, arcMaterial, PlayerLayer);
                    markerGo.transform.localScale = new Vector3(0.7f, 0.02f, 0.7f);
                    markerGo.SetActive(false);
                    marker = markerGo.transform;
                    changed = true;
                }

                var so = new SerializedObject(thrower);
                if (!TrySetReference(so, "_bombConfig", bombConfig, ref changed)
                    || !TrySetReference(so, "_bombPrefab", bombPrefab.GetComponent<SplatBomb>(), ref changed)
                    || !TrySetReference(so, "_inkstrikeConfig", inkstrikeConfig, ref changed)
                    || !TrySetReference(so, "_devicePrefab", devicePrefab.GetComponent<InkstrikeDevice>(), ref changed)
                    || !TrySetReference(so, "_tornadoPrefab", tornadoPrefab.GetComponent<Tornado>(), ref changed)
                    || !TrySetReference(so, "_throwOrigin", muzzle, ref changed)
                    || !TrySetReference(so, "_arc", arc.GetComponent<LineRenderer>(), ref changed)
                    || !TrySetReference(so, "_marker", marker, ref changed))
                {
                    return false;
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                var specialGauge = root.GetComponent<PlayerSpecialGauge>();
                if (specialGauge == null)
                {
                    specialGauge = root.AddComponent<PlayerSpecialGauge>();
                    changed = true;
                }
                var soGauge = new SerializedObject(specialGauge);
                if (!TrySetReference(soGauge, "_config", inkstrikeConfig, ref changed))
                {
                    return false;
                }
                soGauge.ApplyModifiedPropertiesWithoutUndo();

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[M4SETUP] Player prefab 掛上 ThrowController / ThrowArc / ThrowMarker / PlayerSpecialGauge 並接線");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        // 步驟 7:場景——靶場(牆 + 三個假人)+ HUD。已存在的物件不動(設計師可能已手動移位)。
        private static bool EnsureScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Fail("使用者取消切換場景");
                    return false;
                }
                scene = EditorSceneManager.OpenScene(ScenePath);
            }
            var dummyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DummyPrefabPath);
            if (dummyPrefab == null)
            {
                Fail("找不到 TargetDummy.prefab");
                return false;
            }

            GameObject range = GameObject.Find("TargetRange");
            if (range == null)
            {
                range = new GameObject("TargetRange");
            }
            if (range.transform.Find("TargetWall") == null)
            {
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "TargetWall";
                wall.transform.SetParent(range.transform, false);
                wall.transform.position = TargetWallPosition;
                wall.transform.localScale = TargetWallScale;
                Debug.Log("[M4SETUP] 放置 TargetWall");
            }
            EnsureDummy(range, dummyPrefab, "Dummy_Near", DummyNearPosition, false);
            EnsureDummy(range, dummyPrefab, "Dummy_Far", DummyFarPosition, false);
            EnsureDummy(range, dummyPrefab, "Dummy_Moving", DummyMovingPosition, true);

            // HUD:傷害數字(slot 於執行期建立,不需場景物件)
            GameObject hud = GameObject.Find("HudCanvas");
            if (hud == null)
            {
                Fail("找不到 HudCanvas");
                return false;
            }
            if (hud.GetComponent<DamagePopupHud>() == null)
            {
                hud.AddComponent<DamagePopupHud>();
                Debug.Log("[M4SETUP] HudCanvas 掛上 DamagePopupHud");
            }
            if (!EnsureSpecialHud(hud))
            {
                return false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        // 大招能量條:仿 InkBarBG/InkBarFill 往下一排,加狀態文字;SpecialHud 接 Player 實例的 PlayerSpecialGauge。
        private static bool EnsureSpecialHud(GameObject hud)
        {
            GameObject player = GameObject.Find("Player");
            var gauge = player != null ? player.GetComponent<PlayerSpecialGauge>() : null;
            if (gauge == null)
            {
                Fail("場景 Player 上找不到 PlayerSpecialGauge(prefab 未接好?)");
                return false;
            }
            RectTransform bg = EnsureUiElement(hud.transform, "SpecialBarBG", typeof(Image),
                new Vector2(24f, -122f), new Vector2(244f, 22f), out bool bgCreated);
            if (bgCreated)
            {
                bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            }
            RectTransform fill = EnsureUiElement(bg, "SpecialBarFill", typeof(Image),
                new Vector2(2f, -2f), new Vector2(240f, 18f), out bool fillCreated);
            if (fillCreated)
            {
                fill.GetComponent<Image>().color = new Color(1f, 0.85f, 0.2f, 0.95f);
            }
            RectTransform ready = EnsureUiElement(hud.transform, "SpecialReadyText", typeof(Text),
                new Vector2(24f, -150f), new Vector2(300f, 26f), out bool readyCreated);
            if (readyCreated)
            {
                var text = ready.GetComponent<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.color = new Color(1f, 0.9f, 0.3f, 1f);
                text.fontSize = 20;
                text.raycastTarget = false;
                text.text = string.Empty;
            }
            var specialHud = hud.GetComponent<SpecialHud>();
            bool changed = false;
            if (specialHud == null)
            {
                specialHud = hud.AddComponent<SpecialHud>();
                changed = true;
            }
            var so = new SerializedObject(specialHud);
            if (!TrySetReference(so, "_gauge", gauge, ref changed)
                || !TrySetReference(so, "_fill", fill, ref changed)
                || !TrySetReference(so, "_readyText", ready.GetComponent<Text>(), ref changed))
            {
                return false;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            if (changed || bgCreated || fillCreated || readyCreated)
            {
                Debug.Log("[M4SETUP] HudCanvas 建立大招能量條並掛上 SpecialHud");
            }
            return true;
        }

        // uGUI 元件(左上錨點):已存在就回傳原物件不動。
        private static RectTransform EnsureUiElement(Transform parent, string name, System.Type graphicType,
            Vector2 anchoredPosition, Vector2 size, out bool created)
        {
            created = false;
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return (RectTransform)existing;
            }
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), graphicType);
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            created = true;
            return rect;
        }

        private static void EnsureDummy(GameObject range, GameObject prefab, string name, Vector3 position, bool patrol)
        {
            if (range.transform.Find(name) != null)
            {
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, range.scene);
            instance.name = name;
            instance.transform.SetParent(range.transform, false);
            instance.transform.position = position;
            if (patrol)
            {
                instance.AddComponent<DummyPatrol>();
            }
            Debug.Log($"[M4SETUP] 放置 {name} @ {position}");
        }
    }
}
