using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SaturdayGame.Chess.View
{
    /// <summary>
    /// 屏幕 UI（按草图）：左边 “NEW” 是三选一的规则卡，右边是规则书，中间是棋盘；
    /// 上方是状态栏，下方是对白字幕。全部在代码里生成，方便以后整体替换成美术版。
    /// </summary>
    public sealed class ChessHud : MonoBehaviour
    {
        static readonly Color Paper = new Color(0.95f, 0.92f, 0.84f, 0.96f);
        static readonly Color Ink = new Color(0.16f, 0.12f, 0.1f);

        public event Action<int> RuleCardClicked;
        public event Action SkipClicked;
        public event Action PlayAgainClicked;
        public event Action BackClicked;

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

        public static ChessHud Create(Transform parent)
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
            hud.Build();
            return hud;
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = (RectTransform)transform;

            // 左栏：NEW
            var left = Panel(root, "New", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(420, -48), Paper);
            Label(left, "Title", "NEW", 44, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -18), new Vector2(-48, 60));
            newHintText = Label(left, "Hint", "", 22, FontStyle.Italic, TextAnchor.UpperLeft, new Vector2(24, -84), new Vector2(-48, 120));
            cardsRoot = Rect(left, "Cards", new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -126), new Vector2(-40, -146));
            var layout = cardsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            // 右栏：规则书
            var right = Panel(root, "RuleBook", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-24, 0), new Vector2(420, -48), Paper);
            Label(right, "Title", "Rule Book", 40, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(24, -18), new Vector2(-48, 60));
            ruleBookText = Label(right, "Rules", "", 21, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(24, -84), new Vector2(-48, -110));
            ruleBookText.rectTransform.anchorMin = new Vector2(0, 0);
            ruleBookText.rectTransform.sizeDelta = new Vector2(-48, -110);

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

        public void ShowOffers(IReadOnlyList<RuleBase> rules)
        {
            HideOffers();
            newHintText.text = "Pick a house rule:";
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
            sb.Append("  • capture the king to win\n");
            sb.Append("  • no castling, no en passant\n");
            sb.Append("  • pawns always become queens\n");
            foreach (var rule in ruleBook)
            {
                sb.Append($"\n<b>{RuleCatalog.StageNames[rule.Stage]}: {rule.Title}</b>\n");
                sb.Append($"<size=18>{rule.Description}</size>\n");
            }
            if (ruleBook.Count < RuleCatalog.StageCount) sb.Append("\n<i><size=18>(the rest of the pages are blank)</size></i>");
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
