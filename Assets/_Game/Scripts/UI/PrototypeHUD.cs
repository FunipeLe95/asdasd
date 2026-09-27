using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Voron.Player;

namespace Voron.UI
{
    public sealed class PrototypeHUD : MonoBehaviour
    {
        private static readonly Color Paper = new Color(0.76f, 0.73f, 0.62f, 1f);
        private static readonly Color DullPaper = new Color(0.55f, 0.53f, 0.46f, 1f);
        private static readonly Color Rust = new Color(0.55f, 0.23f, 0.17f, 1f);

        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Font font;
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameObject hudRoot;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;
        [SerializeField] private GameObject messageRoot;
        [SerializeField] private Text messageText;
        [SerializeField] private Text controlsText;

        private readonly List<Text> _texts = new List<Text>();
        private float _messageTimer;
        private string _prompt;
        private string _message;
        private bool _lastGamepadMode;

        public string CurrentPrompt => _prompt;
        public string CurrentMessage => _message;

        private void Awake()
        {
            EnsureBuilt();
            ApplyFont();
        }

        private void Update()
        {
            if (_messageTimer > 0f)
            {
                _messageTimer -= Time.unscaledDeltaTime;
                if (_messageTimer <= 0f)
                {
                    _message = null;
                    messageText.text = string.Empty;
                    messageRoot.SetActive(false);
                }
            }

            if (input != null && input.UsingGamepad != _lastGamepadMode)
            {
                _lastGamepadMode = input.UsingGamepad;
                RefreshControlsText();
            }
        }

        public void Configure(PlayerInputReader inputReader, Font uiFont)
        {
            input = inputReader;
            font = uiFont;
            EnsureBuilt();
            ApplyFont();
            _lastGamepadMode = input != null && input.UsingGamepad;
            RefreshControlsText();
        }

        public void SetPrompt(string prompt)
        {
            EnsureBuilt();
            _prompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt;
            promptText.text = _prompt ?? string.Empty;
            promptRoot.SetActive(_prompt != null);
        }

        public void ShowMessage(string message)
        {
            EnsureBuilt();
            _message = string.IsNullOrWhiteSpace(message) ? null : message;
            _messageTimer = _message == null ? 0f : 3.2f;
            messageText.text = _message ?? string.Empty;
            messageRoot.SetActive(_message != null);
        }

        private void EnsureBuilt()
        {
            if (canvas == null)
            {
                Transform existing = transform.Find("Prototype HUD Canvas");
                canvas = existing != null ? existing.GetComponent<Canvas>() : null;
            }

            if (canvas == null)
            {
                canvas = RuntimeUIFactory.CreateOverlayCanvas(transform, "Prototype HUD Canvas", 20);
            }

            if (hudRoot == null)
            {
                RectTransform root = RuntimeUIFactory.CreateRect(
                    canvas.transform,
                    "HUD",
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);
                hudRoot = root.gameObject;
            }

            RectTransform reticle = GetOrCreateRect(hudRoot.transform, "Reticle", new Vector2(9f, 9f));
            SetAnchored(reticle, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Image reticleImage = reticle.GetComponent<Image>();
            if (reticleImage == null)
            {
                reticleImage = reticle.gameObject.AddComponent<Image>();
            }
            reticleImage.color = new Color(Paper.r, Paper.g, Paper.b, 0.72f);
            reticleImage.raycastTarget = false;

            RectTransform prompt = GetOrCreateRect(hudRoot.transform, "Interaction Prompt", new Vector2(410f, 30f));
            SetAnchored(prompt, new Vector2(0f, 52f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            Image promptBackground = prompt.GetComponent<Image>();
            if (promptBackground == null)
            {
                promptBackground = prompt.gameObject.AddComponent<Image>();
            }
            promptBackground.color = new Color(0.018f, 0.02f, 0.019f, 0.78f);
            promptBackground.raycastTarget = false;
            promptRoot = prompt.gameObject;

            promptText = GetOrCreateText(
                prompt,
                "Prompt Text",
                new Vector2(390f, 26f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                17,
                Paper,
                TextAnchor.MiddleCenter,
                string.Empty);

            RectTransform message = GetOrCreateRect(hudRoot.transform, "Message", new Vector2(540f, 52f));
            SetAnchored(message, new Vector2(0f, 91f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            Image messageBackground = message.GetComponent<Image>();
            if (messageBackground == null)
            {
                messageBackground = message.gameObject.AddComponent<Image>();
            }
            messageBackground.color = new Color(0.018f, 0.02f, 0.019f, 0.74f);
            messageBackground.raycastTarget = false;
            messageRoot = message.gameObject;
            messageText = GetOrCreateText(
                message,
                "Message Text",
                new Vector2(510f, 44f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                15,
                Paper,
                TextAnchor.MiddleCenter,
                string.Empty);

            RectTransform controls = GetOrCreateRect(hudRoot.transform, "Controls Hint", new Vector2(300f, 22f));
            SetAnchored(controls, new Vector2(-17f, 14f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            controlsText = GetOrCreateText(
                controls,
                "Controls Text",
                new Vector2(296f, 20f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                11,
                DullPaper,
                TextAnchor.MiddleRight,
                "[TAB] FILE   [F] LIGHT");

            if (promptRoot != null && string.IsNullOrEmpty(_prompt))
            {
                promptRoot.SetActive(false);
            }
            if (messageRoot != null && string.IsNullOrEmpty(_message))
            {
                messageRoot.SetActive(false);
            }
            RefreshControlsText();
        }

        private void RefreshControlsText()
        {
            if (controlsText == null)
            {
                return;
            }

            bool gamepad = input != null && input.UsingGamepad;
            controlsText.text = gamepad
                ? "[Y] FILE   [X] LIGHT"
                : "[TAB] FILE   [F] LIGHT";
        }

        private void ApplyFont()
        {
            Font resolvedFont = RuntimeUIFactory.ResolveFont(font);
            for (int i = 0; i < _texts.Count; i++)
            {
                if (_texts[i] != null)
                {
                    _texts[i].font = resolvedFont;
                }
            }
        }

        private Text GetOrCreateText(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 position,
            Vector2 pivot,
            int fontSize,
            Color color,
            TextAnchor alignment,
            string value)
        {
            Transform existing = parent.Find(name);
            Text text = existing != null ? existing.GetComponent<Text>() : null;
            if (text == null)
            {
                text = RuntimeUIFactory.CreateText(parent, name, font, fontSize, color, alignment, value);
            }
            else
            {
                text.fontSize = fontSize;
                text.color = color;
                text.alignment = alignment;
                text.text = value;
            }

            text.rectTransform.anchorMin = pivot;
            text.rectTransform.anchorMax = pivot;
            text.rectTransform.pivot = pivot;
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            if (!_texts.Contains(text))
            {
                _texts.Add(text);
            }
            return text;
        }

        private static RectTransform GetOrCreateRect(Transform parent, string name, Vector2 size)
        {
            Transform existing = parent.Find(name);
            RectTransform rect = existing as RectTransform;
            if (rect == null)
            {
                rect = RuntimeUIFactory.CreateRect(
                    parent,
                    name,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    size);
            }
            else
            {
                rect.sizeDelta = size;
            }
            return rect;
        }

        private static void SetAnchored(RectTransform rect, Vector2 position, Vector2 pivot, Vector2 anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
        }
    }
}
