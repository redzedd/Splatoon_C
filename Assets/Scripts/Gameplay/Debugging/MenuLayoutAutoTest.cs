using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.UI.Menu;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SplatoonC.Gameplay.Debugging
{
    // 選單版面驗收 + 截圖(主選單場景):每一頁在 1920×1080 與 1280×720 各拍一張(Logs/shots/menu_*.png),並自動檢查:
    //   - 所有可選元件與文字都在畫面內(捲動區內被遮住的卡片除外)
    //   - 同一頁的可選元件互不重疊
    //   - 單行文字的實際寬度不超出它的框、會換行的段落高度不超出框(不會被截字)
    //   - 截圖不是空白(有內容)
    // 武器頁另外用 30 組、11 種類的預覽清單拍一次(捲動 + 分頁全開),確認 1~30 組都排得下。
    // 最後進訓練場拍暫停選單(首頁 + 設定)。
    [AutoTest(Scene = MenuFlowAutoTest.MenuScene, Timeout = 180f, Order = 301)]
    public sealed class MenuLayoutAutoTest : AutoTestBase
    {
        private const string Folder = "Logs/shots";
        private static readonly Vector2Int[] Resolutions = { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) };

        private readonly Vector3[] _corners = new Vector3[4];
        private readonly List<string> _problems = new List<string>();

        public static void Run()
        {
            Launch<MenuLayoutAutoTest>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private static IEnumerator WaitReal(System.Func<bool> condition, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < end)
            {
                yield return null;
            }
        }

        private IEnumerator Start()
        {
            yield return WaitReal(() => MainMenuController.Instance != null && MainMenuController.Instance.Navigator != null
                                        && MainMenuController.Instance.Navigator.Current != null, 8f);
            MainMenuController menu = MainMenuController.Instance;
            if (menu == null)
            {
                FailSetup("沒有 MainMenuController(需在 MainMenu 場景)");
                yield break;
            }
            Directory.CreateDirectory(Folder);
            MenuNavigator nav = menu.Navigator;

            yield return Page("title", menu.Title, () => nav.ResetTo(menu.Title));
            yield return Page("home", menu.Home, () => nav.ResetTo(menu.Title, menu.Home));
            yield return Page("modes", menu.Modes, () => nav.ResetTo(menu.Home, menu.Modes));
            yield return Page("stages", menu.Stages, () => nav.ResetTo(menu.Home, menu.Modes, menu.Stages));
            yield return Page("weapons", menu.Weapons, () => nav.ResetTo(menu.Home, menu.Modes, menu.Stages, menu.Weapons));
            yield return Page("setup", menu.Setup, () => nav.ResetTo(menu.Home, menu.Modes, menu.Stages, menu.Weapons, menu.Setup));
            yield return Page("settings", menu.Settings, () => nav.ResetTo(menu.Home, menu.Settings));
            yield return Page("confirm", menu.Confirm, () =>
            {
                nav.ResetTo(menu.Title, menu.Home);
                menu.Confirm.Show(nav, "要結束遊戲嗎?", "結束", null);
            });

            // 30 組、11 種類的預覽清單:卡片縮小 + 捲動、分頁全開
            WeaponCatalog preview = PreviewCatalog(30);
            if (preview != null)
            {
                MainMenuController.CatalogOverride = preview;
                menu.SelectedKit = preview.Kits[0];
                yield return Page("weapons30", menu.Weapons, () => nav.ResetTo(menu.Home, menu.Modes, menu.Stages, menu.Weapons));
                Check("30 組:卡片全部建立、需要捲動、分頁 = 全部 + 11 種",
                    menu.Weapons.Grid.Cards.Count == 30 && menu.Weapons.Grid.Layout.Scrolls && menu.Weapons.TabCount == 12,
                    $"cards={menu.Weapons.Grid.Cards.Count} scrolls={menu.Weapons.Grid.Layout.Scrolls} tabs={menu.Weapons.TabCount}");
                // 選到最後一張:格子捲到底
                nav.Select(menu.Weapons.Grid.Cards[29]);
                yield return new WaitForSecondsRealtime(0.6f);
                RectTransform content = menu.Weapons.Grid.Content;
                float scroll = content.anchoredPosition.y;
                float expected = menu.Weapons.Grid.Layout.ContentHeight - menu.Weapons.Grid.Height;
                Check("選到最後一張卡片時自動捲到底", Mathf.Abs(scroll - expected) < 40f, $"捲動 {scroll:0} 預期 ~{expected:0}");
                yield return Page("weapons30_bottom", menu.Weapons, null);
                menu.Weapons.SetFilter((int)WeaponClass.Charger);
                yield return Page("weapons30_charger", menu.Weapons, null);
                bool allChargers = menu.Weapons.VisibleKits.Count > 0;
                foreach (WeaponKit kit in menu.Weapons.VisibleKits)
                {
                    allChargers &= kit.Main.Class == WeaponClass.Charger;
                }
                Check("種類分頁篩選:狙擊槍", allChargers, $"visible={menu.Weapons.VisibleKits.Count}");
                MainMenuController.CatalogOverride = null;
                menu.SelectedKit = WeaponCatalog.Load() != null ? WeaponCatalog.Load().Default : null;
            }

            // 暫停選單(訓練場)
            MatchSettings.LastLaunch = LaunchKind.Training;
            nav.ResetTo(menu.Home);
            SceneTransition.Load(MainMenuController.TrainingScene, "訓練場", MenuTheme.Accent);
            yield return WaitReal(() => !SceneTransition.Busy && SceneManager.GetActiveScene().name == MainMenuController.TrainingScene
                                        && PauseMenu.Instance != null, 40f);
            PauseMenu pause = PauseMenu.Instance;
            if (pause == null)
            {
                Check("訓練場有暫停選單", false, SceneManager.GetActiveScene().name);
            }
            else
            {
                yield return new WaitForSecondsRealtime(1f);
                pause.Open();
                yield return Page("pause", pause.Root, null);
                pause.Navigator.Push(pause.Settings);
                yield return Page("pause_settings", pause.Settings, null);
                pause.Navigator.Back();
                yield return null;
                pause.Resume();
            }
            Finish();
        }

        // 顯示一頁(show 可為 null = 已顯示),等動畫收尾,各解析度拍照 + 檢查
        private IEnumerator Page(string name, MenuPage page, System.Action show)
        {
            show?.Invoke();
            yield return new WaitForSecondsRealtime(0.55f);
            foreach (Vector2Int resolution in Resolutions)
            {
                yield return Capture(name, page, resolution.x, resolution.y);
            }
        }

        private IEnumerator Capture(string name, MenuPage page, int width, int height)
        {
            Camera sceneCamera = Camera.main;
            Camera cam = sceneCamera;
            GameObject temp = null;
            if (cam == null)
            {
                temp = new GameObject("ShotCamera");
                cam = temp.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.orthographic = true;
            }
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var switched = new List<Canvas>();
            foreach (Canvas canvas in FindObjectsByType<Canvas>())
            {
                if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.isActiveAndEnabled)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    switched.Add(canvas);
                }
            }
            RenderTexture previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            // 截圖時關掉相機後製:遊戲裡 Overlay UI 不吃色調映射,改成相機空間後會被洗白
            bool? previousPost = SetPostProcessing(cam, false);
            // CanvasScaler 在 Update 依新尺寸重算縮放;文字的字形解析度跟著縮放,全部標髒重建(否則沿用批次模式小視窗的低解析字形)
            yield return null;
            foreach (Canvas canvas in switched)
            {
                foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>())
                {
                    graphic.SetAllDirty();
                }
            }
            yield return null;
            Canvas.ForceUpdateCanvases();

            string label = $"{name}@{width}x{height}";
            Canvas pageCanvas = page.Root.GetComponentInParent<Canvas>().rootCanvas;
            CheckLayout(label, page, pageCanvas);

            cam.Render();
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previousActive;
            string path = $"{Folder}/menu_{name}_{width}x{height}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            float contrast = Contrast(texture);
            Check($"截圖有內容 {label}", contrast > 0.03f, $"{path} 亮度標準差={contrast:0.000}");
            Destroy(texture);

            cam.targetTexture = previousTarget;
            if (previousPost.HasValue)
            {
                SetPostProcessing(cam, previousPost.Value);
            }
            foreach (Canvas canvas in switched)
            {
                if (canvas != null)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }
            }
            rt.Release();
            Destroy(rt);
            if (temp != null)
            {
                Destroy(temp);
            }
            yield return null;
        }

        // URP 的 UniversalAdditionalCameraData.renderPostProcessing(Gameplay 組件不引用 URP,用反射);回傳原值
        private static bool? SetPostProcessing(Camera cam, bool enabled)
        {
            Component data = cam.GetComponent("UniversalAdditionalCameraData");
            System.Reflection.PropertyInfo property = data != null ? data.GetType().GetProperty("renderPostProcessing") : null;
            if (property == null)
            {
                return null;
            }
            bool previous = (bool)property.GetValue(data);
            property.SetValue(data, enabled);
            return previous;
        }

        private static float Contrast(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            double sum = 0;
            double sumSq = 0;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += 97)
            {
                float l = (0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b) / 255f;
                sum += l;
                sumSq += l * l;
                count++;
            }
            double mean = sum / count;
            return (float)System.Math.Sqrt(System.Math.Max(0.0, sumSq / count - mean * mean));
        }

        // ── 版面檢查 ──

        private void CheckLayout(string label, MenuPage page, Canvas canvas)
        {
            _problems.Clear();
            var canvasRect = (RectTransform)canvas.transform;
            Rect screen = canvasRect.rect;
            float scale = canvas.scaleFactor;
            Vector2 expectedSize = new Vector2(canvas.worldCamera.pixelWidth, canvas.worldCamera.pixelHeight) / scale;
            if (Vector2.Distance(screen.size, expectedSize) > 2f)
            {
                _problems.Add($"畫布尺寸 {screen.size} ≠ 解析度/縮放 {expectedSize}");
            }

            var selectables = new List<(Selectable, Rect)>();
            foreach (Selectable selectable in page.Root.GetComponentsInChildren<Selectable>())
            {
                if (!selectable.isActiveAndEnabled || selectable.navigation.mode == Navigation.Mode.None && IsFullScreen(selectable))
                {
                    continue;
                }
                Rect rect = CanvasRect(canvasRect, (RectTransform)selectable.transform);
                if (IsMaskedOut(selectable.transform, canvasRect, rect))
                {
                    continue;
                }
                if (!Inside(screen, rect, 2f))
                {
                    _problems.Add($"{selectable.name} 超出畫面 {Fmt(rect)}");
                }
                selectables.Add((selectable, rect));
            }
            for (int i = 0; i < selectables.Count; i++)
            {
                for (int j = i + 1; j < selectables.Count; j++)
                {
                    Rect a = selectables[i].Item2;
                    Rect b = selectables[j].Item2;
                    float overlapX = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
                    float overlapY = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (overlapX > 2f && overlapY > 2f)
                    {
                        _problems.Add($"{selectables[i].Item1.name} 與 {selectables[j].Item1.name} 重疊");
                    }
                }
            }

            // 文字:本頁 + 提示列
            var texts = new List<Text>(page.Root.GetComponentsInChildren<Text>());
            Transform hints = canvas.transform.Find("HintBar");
            if (hints != null)
            {
                texts.AddRange(hints.GetComponentsInChildren<Text>());
            }
            foreach (Text text in texts)
            {
                if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }
                var rt = (RectTransform)text.transform;
                Rect rect = CanvasRect(canvasRect, rt);
                if (IsMaskedOut(text.transform, canvasRect, rect))
                {
                    continue;
                }
                if (!Inside(screen, rect, 2f))
                {
                    _problems.Add($"文字「{Short(text.text)}」超出畫面 {Fmt(rect)}");
                }
                if (text.horizontalOverflow == HorizontalWrapMode.Overflow)
                {
                    float preferred = text.preferredWidth;
                    if (preferred > rt.rect.width + 4f)
                    {
                        _problems.Add($"文字「{Short(text.text)}」寬 {preferred:0} > 框 {rt.rect.width:0}");
                    }
                }
                else
                {
                    float preferred = text.preferredHeight;
                    if (preferred > rt.rect.height + 4f)
                    {
                        _problems.Add($"段落「{Short(text.text)}」高 {preferred:0} > 框 {rt.rect.height:0}");
                    }
                }
            }

            var evidence = new StringBuilder();
            evidence.Append($"可選元件 {selectables.Count}、文字 {texts.Count}");
            for (int i = 0; i < _problems.Count && i < 12; i++)
            {
                evidence.Append(" | ").Append(_problems[i]);
            }
            Check($"版面 {label}", _problems.Count == 0, evidence.ToString());
        }

        private static bool IsFullScreen(Selectable selectable)
        {
            var rt = (RectTransform)selectable.transform;
            return rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
        }

        // 元件四角 → 畫布座標的外接矩形
        private Rect CanvasRect(RectTransform canvasRect, RectTransform target)
        {
            target.GetWorldCorners(_corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRect.InverseTransformPoint(_corners[i]);
                min = Vector2.Min(min, local);
                max = Vector2.Max(max, local);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // 在 RectMask2D(捲動區)裡且不完全落在可視範圍內:被遮住的部分不算
        private bool IsMaskedOut(Transform element, RectTransform canvasRect, Rect rect)
        {
            RectMask2D mask = element.GetComponentInParent<RectMask2D>();
            if (mask == null)
            {
                return false;
            }
            Rect maskRect = CanvasRect(canvasRect, mask.rectTransform);
            // 遮罩四周留了 24 的邊(選取放大/外框)
            maskRect.xMin -= 24f;
            maskRect.yMin -= 24f;
            maskRect.xMax += 24f;
            maskRect.yMax += 24f;
            return !Inside(maskRect, rect, 2f);
        }

        private static bool Inside(Rect outer, Rect inner, float tolerance)
        {
            return inner.xMin >= outer.xMin - tolerance && inner.xMax <= outer.xMax + tolerance
                   && inner.yMin >= outer.yMin - tolerance && inner.yMax <= outer.yMax + tolerance;
        }

        private static string Fmt(Rect r)
        {
            return $"({r.xMin:0},{r.yMin:0})~({r.xMax:0},{r.yMax:0})";
        }

        private static string Short(string s)
        {
            s = s.Replace("\n", " ");
            return s.Length > 14 ? s.Substring(0, 14) + "…" : s;
        }

        // 30 組預覽武器:11 種類輪流,副武器/大招沿用正式清單的第一組
        private static WeaponCatalog PreviewCatalog(int count)
        {
            WeaponCatalog real = WeaponCatalog.Load();
            WeaponKit basis = real != null ? real.Default : null;
            if (basis == null || !basis.IsValid)
            {
                return null;
            }
            var configs = new Dictionary<WeaponClass, MainWeaponConfig>();
            var kits = new List<WeaponKit>();
            string[] names =
            {
                "斯普拉射擊槍", "斯普拉滾筒", "斯普拉狙擊槍", "飛濺潑桶", "斯普拉旋轉槍", "斯普拉雙槍", "遮陽防空傘", "新星爆破槍",
                "巴勒特筆刷", "三發獵魚弓", "工作刮水刀",
            };
            for (int i = 0; i < count; i++)
            {
                var weaponClass = (WeaponClass)(i % 11);
                if (!configs.TryGetValue(weaponClass, out MainWeaponConfig main))
                {
                    main = weaponClass == WeaponClass.Shooter ? basis.Main : MenuPreviewMainConfig.Create(weaponClass);
                    configs[weaponClass] = main;
                }
                var kit = ScriptableObject.CreateInstance<WeaponKit>();
                kit.hideFlags = HideFlags.DontUnloadUnusedAsset;
                string suffix = i < 11 ? "" : i < 22 ? " 聯名" : " 改";
                kit.Setup("preview_" + i, names[i % 11] + suffix, "Preview " + i, "版面預覽用武器(執行期建立)。第二行說明文字確認換行與高度。",
                    main, basis.Sub, basis.Special, 160f + (i % 7) * 10f, (WeightClass)(i % 3), "射程", 20 + i * 2, "威力",
                    90 - i * 2, "射速", 40 + (i * 7) % 50);
                kits.Add(kit);
            }
            // 同種類排在一起(S3 清單依種類排序)
            kits.Sort((a, b) => ((int)a.Main.Class).CompareTo((int)b.Main.Class));
            var catalog = ScriptableObject.CreateInstance<WeaponCatalog>();
            catalog.hideFlags = HideFlags.DontUnloadUnusedAsset;
            catalog.SetKits(kits);
            return catalog;
        }
    }
}
