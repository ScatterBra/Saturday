using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 桌面布局（按草图）：左边 “NEW” 规则卡区、中间棋盘、右边规则书，三者平放在同一张桌面上，
    /// 被同一台斜视相机拍到。规则卡区和规则书是平躺在桌面上的 World Space Canvas。
    /// 状态栏、对白字幕、Skip 按钮、结束面板仍然是屏幕最上层的 UI。全部在代码里生成。
    /// </summary>
    public sealed class ChessHud : MonoBehaviour
    {
        /// <summary>桌面上两块区域的尺寸和位置（世界单位，一格 = 1）。DeskCameraFit 用它来框住整个布局。</summary>
        public static class DeskLayout
        {
            public const float PanelWidth = 4.2f;
            public const float PanelDepth = 9f;
            /// <summary>左右两块区域中心离棋盘中心的距离。</summary>
            public const float PanelCenterX = 7f;
            /// <summary>Canvas 每个世界单位的像素数（UI 尺寸按像素写）。</summary>
            public const float PixelsPerUnit = 100f;
            public const float HalfExtentX = PanelCenterX + PanelWidth / 2 + 0.2f;
            public const float HalfExtentZ = PanelDepth / 2 + 0.1f;
        }

        static readonly Color Paper = new Color(0.95f, 0.92f, 0.84f, 0.96f);
        static readonly Color Ink = new Color(0.16f, 0.12f, 0.1f);

        public event Action<int> RuleCardClicked;
        public event Action SkipClicked;
        public event Action PlayAgainClicked;
        public event Action BackClicked;
        /// <summary>点了被吃区里的一枚棋子（只有在复活技能等待点选时才能点）。</summary>
        public event Action<int> LostPieceClicked;

        Font font;
        Text statusText;
        Text newHintText;
        Text ruleBookText;
        Text captionSpeaker;
        Text captionText;
        Text gameOverText;
        GameObject captionRoot;
        GameObject skipButton;
        GameObject gameOverRoot;
        GameObject backButton;
        RectTransform playAgainButton;
        RectTransform cardsRoot;
        readonly List<GameObject> cards = new List<GameObject>();
        readonly List<RuleBase> ruleBook = new List<RuleBase>();
        RectTransform lostRoot;
        Image stormOverlay;
        PieceArt art;

        public static ChessHud Create(Transform parent, Camera deskCamera, PieceArt art = null)
        {
            var go = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = go.AddComponent<ChessHud>();
            hud.art = art;
            hud.Build(parent, deskCamera);
            return hud;
        }

        /// <summary>平躺在桌面上的一块 World Space Canvas，中心在 (centerX, 0, 0)。</summary>
        static RectTransform DeskCanvas(Transform parent, string name, float centerX, Camera camera)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(DeskLayout.PanelWidth, DeskLayout.PanelDepth) * DeskLayout.PixelsPerUnit;
            // 贴在桌面上（桌面顶面 y = -0.08），比棋盘格子（y = 0）略低，像放在桌上的纸
            rt.localPosition = new Vector3(centerX, -0.075f, 0f);
            // Canvas 默认面朝 -Z，绕 X 转 90° 后面朝上、顶边朝远处，平躺在桌面上
            rt.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rt.localScale = Vector3.one / DeskLayout.PixelsPerUnit;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            // 世界空间里的文字按 3 倍分辨率栅格化，斜着看也不糊
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            return rt;
        }

        void Build(Transform deskParent, Camera deskCamera)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = (RectTransform)transform;

            // 左边：NEW 规则卡区（平放在桌面上）
            var leftCanvas = DeskCanvas(deskParent, "Desk New", -DeskLayout.PanelCenterX, deskCamera);
            var left = Panel(leftCanvas, "New", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Paper);
            Label(left, "Title", "NEW", 44, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -18), new Vector2(-48, 60));
            newHintText = Label(left, "Hint", "", 22, FontStyle.Italic, TextAnchor.UpperLeft, new Vector2(24, -84), new Vector2(-48, 120));
            cardsRoot = Rect(left, "Cards", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -126), new Vector2(-40, -146));
            var layout = cardsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            // 右边：规则书（平放在桌面上）
            var rightCanvas = DeskCanvas(deskParent, "Desk RuleBook", DeskLayout.PanelCenterX, deskCamera);
            var right = Panel(rightCanvas, "RuleBook", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Paper);
            Label(right, "Title", "Rule Book", 40, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -18), new Vector2(-48, 60));
            ruleBookText = Label(right, "Rules", "", 26, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(24, -84), new Vector2(-48, -110));
            // 规则多了就自动缩小字号，不会压到下面的被吃区
            ruleBookText.resizeTextForBestFit = true;
            ruleBookText.resizeTextMinSize = 12;
            ruleBookText.resizeTextMaxSize = 26;
            ruleBookText.verticalOverflow = VerticalWrapMode.Truncate;
            ruleBookText.rectTransform.anchorMin = new Vector2(0, 0.36f);
            ruleBookText.rectTransform.sizeDelta = new Vector2(-48, -110);

            // 规则书下半部分：我方被吃的棋子（复活技能在这里点选）
            Label(right, "LostTitle", "Lost pieces", 26, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -582), new Vector2(-48, 34));
            lostRoot = Rect(right, "Lost", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(-40, 260));
            var grid = lostRoot.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(84, 84);
            grid.spacing = new Vector2(10, 10);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;

            // 上方状态栏
            var statusPanel = Panel(root, "Status", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(980, 56), new Color(0.1f, 0.08f, 0.08f, 0.8f));
            statusText = Label(statusPanel, "Text", "", 26, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, stretch: true);
            statusText.color = Color.white;

            // 下方字幕
            var caption = Panel(root, "Caption", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(980, 96), new Color(0.08f, 0.06f, 0.06f, 0.88f));
            captionRoot = caption.gameObject;
            captionSpeaker = Label(caption, "Speaker", "", 22, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -10), new Vector2(-48, 30));
            captionSpeaker.color = new Color(1f, 0.8f, 0.45f);
            captionText = Label(caption, "Line", "", 26, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(24, -40), new Vector2(-48, 50));
            captionText.color = Color.white;
            captionRoot.SetActive(false);

            // 跳过按钮（城堡选藏身棋子时用）
            skipButton = Button(root, "Skip", "Skip", new Vector2(0.5f, 0), new Vector2(0, 128), new Vector2(220, 56), () => SkipClicked?.Invoke()).gameObject;
            skipButton.SetActive(false);

            // 结束面板
            var dim = Panel(root, "GameOver", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0, 0, 0, 0.55f));
            gameOverRoot = dim.gameObject;
            var box = Panel(dim, "Box", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 320), Paper);
            gameOverText = Label(box, "Text", "", 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0, -30), new Vector2(-60, 160));
            var buttonRow = Rect(box, "Buttons", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(600, 64));
            playAgainButton = (RectTransform)Button(buttonRow, "PlayAgain", "Play again", new Vector2(0.5f, 0.5f), new Vector2(-150, 0), new Vector2(260, 64), () => PlayAgainClicked?.Invoke()).transform;
            backButton = Button(buttonRow, "Back", "Back to office", new Vector2(0.5f, 0.5f), new Vector2(150, 0), new Vector2(260, 64), () => BackClicked?.Invoke()).gameObject;
            gameOverRoot.SetActive(false);

            // 风暴：盖住整个画面的沙尘，透明度由控制器控制
            stormOverlay = Panel(root, "Storm", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.62f, 0.55f, 0.45f, 0f)).GetComponent<Image>();
            stormOverlay.raycastTarget = false;
        }

        // ---------- 对外接口 ----------

        public void ResetForNewGame()
        {
            ruleBook.Clear();
            RefreshRuleBook();
            HideOffers();
            HideCaption();
            ShowSkip(false);
            gameOverRoot.SetActive(false);
        }

        public void SetStatus(string text) => statusText.text = text;

        public void SetNewHint(string text) => newHintText.text = text;

        public void ShowOffers(IReadOnlyList<RuleBase> rules, OfferKind kind = OfferKind.StageCard)
        {
            HideOffers();
            switch (kind)
            {
                case OfferKind.Emergency: newHintText.text = "Check! Pick a skill, or skip:"; break;
                case OfferKind.Stage3: newHintText.text = "Pick a skill, or skip:"; break;
                default: newHintText.text = "Pick a house rule:"; break;
            }
            for (int i = 0; i < rules.Count; i++)
            {
                int index = i;
                cards.Add(Card(rules[i], () => RuleCardClicked?.Invoke(index)));
            }
        }

        public void HideOffers()
        {
            foreach (var card in cards) Destroy(card);
            cards.Clear();
        }

        public void AddRule(RuleBase rule)
        {
            ruleBook.Add(rule);
            RefreshRuleBook();
        }

        public void ShowCaption(string speaker, string text)
        {
            captionRoot.SetActive(true);
            captionSpeaker.text = speaker ?? "";
            captionText.text = text;
        }

        public void HideCaption() => captionRoot.SetActive(false);

        public void ShowSkip(bool show) => skipButton.SetActive(show);

        /// <summary>风暴沙尘的浓度，0 = 看不见，1 = 完全盖住画面。</summary>
        public void SetStorm(float amount)
        {
            var c = stormOverlay.color;
            c.a = Mathf.Clamp01(amount);
            stormOverlay.color = c;
        }

        /// <summary>
        /// 刷新规则书下半部分的被吃棋子。pickable 里的棋子可以点（复活技能等待点选时），其余只是显示。
        /// </summary>
        public void SetLostPieces(IEnumerable<Piece> lost, ICollection<int> pickable)
        {
            foreach (Transform child in lostRoot) Destroy(child.gameObject);
            foreach (var piece in lost)
            {
                int id = piece.Id;
                bool canPick = pickable != null && pickable.Contains(id);
                var go = new GameObject($"Lost {piece.Type}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(lostRoot, false);
                var image = go.GetComponent<Image>();
                var sprite = art != null ? art.Resolve(piece).Sprite : null;
                if (sprite != null)
                {
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.color = canPick || pickable == null ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                }
                else
                {
                    image.color = new Color(0.85f, 0.8f, 0.7f, canPick || pickable == null ? 1f : 0.45f);
                    var letter = Label((RectTransform)go.transform, "Letter", PieceValues.Letter(piece.Type), 40, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, stretch: true);
                    letter.color = Ink;
                }
                if (canPick)
                {
                    var outline = go.AddComponent<Outline>();
                    outline.effectColor = new Color(0.2f, 0.6f, 1f);
                    outline.effectDistance = new Vector2(4, 4);
                    go.AddComponent<Button>().onClick.AddListener(() => LostPieceClicked?.Invoke(id));
                }
            }
        }

        public void ShowGameOver(string text, bool canGoBack)
        {
            gameOverText.text = text;
            backButton.SetActive(canGoBack);
            playAgainButton.anchoredPosition = new Vector2(canGoBack ? -150 : 0, 0);
            gameOverRoot.SetActive(true);
        }

        // ---------- 内部 ----------

        void RefreshRuleBook()
        {
            var sb = new StringBuilder();
            sb.Append("<b>Regular chess, except:</b>\n");
            sb.Append("  • get checkmated and you lose\n");
            sb.Append("  • in check? you may use a skill\n");
            sb.Append("  • no castling, no en passant\n");
            sb.Append("  • pawns always become queens\n");
            foreach (var rule in ruleBook)
            {
                sb.Append($"\n<b>{RuleCatalog.StageNames[rule.Stage]}: {rule.Title}</b>\n");
                sb.Append($"{rule.Description}\n");
            }
            if (ruleBook.Count < RuleCatalog.StageCount) sb.Append("\n<i>(the rest of the pages are blank)</i>");
            ruleBookText.text = sb.ToString();
        }

        GameObject Card(RuleBase rule, Action onClick)
        {
            var go = new GameObject($"Card {rule.Id}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(cardsRoot, false);
            go.GetComponent<LayoutElement>().preferredHeight = 230;
            go.GetComponent<Image>().color = KindColor(rule.Kind);
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            var rt = (RectTransform)go.transform;
            Label(rt, "Kind", KindLabel(rule.Kind), 18, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18, -12), new Vector2(-36, 26));
            Label(rt, "Title", rule.Title, 28, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(18, -40), new Vector2(-36, 40));
            Label(rt, "Description", rule.Description, 21, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(18, -86), new Vector2(-36, 140));
            return go;
        }

        static Color KindColor(RuleKind kind)
        {
            switch (kind)
            {
                case RuleKind.Cosmetic: return new Color(0.78f, 0.88f, 0.98f);
                case RuleKind.Buff: return new Color(0.78f, 0.95f, 0.75f);
                case RuleKind.Debuff: return new Color(0.98f, 0.8f, 0.78f);
                default: return new Color(1f, 0.82f, 0.6f);
            }
        }

        static string KindLabel(RuleKind kind)
        {
            switch (kind)
            {
                case RuleKind.Cosmetic: return "LOOKS ONLY";
                case RuleKind.Buff: return "BOOSTS YOU";
                case RuleKind.Debuff: return "HITS YOUR FRIEND";
                default: return "HITS EVERYONE";
            }
        }

        static RectTransform Rect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Panel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size, Color color)
        {
            var rt = Rect(parent, name, anchorMin, anchorMax, pivot, position, size);
            rt.gameObject.AddComponent<Image>().color = color;
            return rt;
        }

        /// <summary>顶部对齐、横向撑满父物体（左右各留边距）的文字。</summary>
        Text Label(RectTransform parent, string name, string text, int size, FontStyle style, TextAnchor anchor,
            Vector2 position, Vector2 sizeDelta, bool stretch = false)
        {
            var rt = stretch
                ? Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), position, sizeDelta)
                : Rect(parent, name, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), position, sizeDelta);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = anchor;
            label.color = Ink;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        Button Button(RectTransform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size, Action onClick)
        {
            var rt = Panel(parent, name, anchor, anchor, new Vector2(0.5f, 0.5f), position, size, new Color(0.25f, 0.2f, 0.18f));
            var button = rt.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick());
            var label = Label(rt, "Text", text, 26, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, stretch: true);
            label.color = Color.white;
            return button;
        }
    }
}
