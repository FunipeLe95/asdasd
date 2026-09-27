using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Voron.Inventory;
using Voron.Player;

namespace Voron.UI
{
    public sealed class InventoryUI : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.035f, 0.039f, 0.039f, 0.97f);
        private static readonly Color Paper = new Color(0.72f, 0.69f, 0.59f, 1f);
        private static readonly Color DullPaper = new Color(0.48f, 0.48f, 0.42f, 1f);
        private static readonly Color Rust = new Color(0.53f, 0.23f, 0.17f, 1f);

        [SerializeField] private InventorySystem inventory;
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private Font font;
        [SerializeField] private Canvas canvas;
        [SerializeField] private GameObject modalRoot;
        [SerializeField] private RectTransform closeHitArea;
        [SerializeField] private Text titleText;
        [SerializeField] private Text closeText;
        [SerializeField] private Text capacityText;
        [SerializeField] private Text controlsText;
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private Text detailNameText;
        [SerializeField] private Text detailTypeText;
        [SerializeField] private Text detailDescriptionText;
        [SerializeField] private Text detailQuantityText;
        [SerializeField] private Image detailIconImage;
        [SerializeField] private Text detailIconGlyph;
        [SerializeField] private List<RectTransform> slotRoots = new List<RectTransform>();
        [SerializeField] private List<Image> slotFrames = new List<Image>();
        [SerializeField] private List<Image> slotBackgrounds = new List<Image>();
        [SerializeField] private List<Image> slotIcons = new List<Image>();
        [SerializeField] private List<Text> slotGlyphs = new List<Text>();
        [SerializeField] private List<Text> slotQuantities = new List<Text>();

        private readonly List<InventoryItem> _items = new List<InventoryItem>();
        private readonly List<Text> _texts = new List<Text>();
        private int _slotCapacity;
        private int _columns = 4;
        private int _selectedIndex = -1;
        private bool _isOpen;
        private bool _navigationHeld;
        private Vector2Int _navigationDirection;
        private float _nextNavigationTime;
        private bool _lastGamepadMode;

        public bool IsOpen => _isOpen;
        public int SelectedIndex => _selectedIndex;
        public ItemData SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex].Data : null;
        public event Action<bool> Toggled;

        private void Awake()
        {
            EnsureBuilt();
            ApplyFont();
            modalRoot.SetActive(_isOpen);
            RefreshInventory(false);
        }

        private void OnEnable()
        {
            SubscribeToInventory();
        }

        private void OnDisable()
        {
            UnsubscribeFromInventory();
        }

        private void OnDestroy()
        {
            UnsubscribeFromInventory();
        }

        private void Update()
        {
            if (input == null)
            {
                return;
            }

            if (input.UsingGamepad != _lastGamepadMode)
            {
                _lastGamepadMode = input.UsingGamepad;
                RefreshControlsText();
            }

            if (input.InventoryPressed)
            {
                SetOpen(!_isOpen);
                return;
            }

            if (!_isOpen)
            {
                return;
            }

            if (input.CancelPressed)
            {
                SetOpen(false);
                return;
            }

            HandleNavigation(input.Navigate);
            HandlePointer();

            if (input.SubmitPressed)
            {
                RefreshDetails();
            }
        }

        public void Configure(InventorySystem inventorySystem, PlayerInputReader inputReader, Font uiFont)
        {
            if (inventory != inventorySystem)
            {
                UnsubscribeFromInventory();
                inventory = inventorySystem;
            }

            input = inputReader;
            font = uiFont;
            EnsureBuilt();
            ApplyFont();
            SubscribeToInventory();
            RefreshInventory(true);
            modalRoot.SetActive(_isOpen);
            _lastGamepadMode = input != null && input.UsingGamepad;
            RefreshControlsText();
        }

        public void SetOpen(bool isOpen)
        {
            EnsureBuilt();
            if (_isOpen == isOpen)
            {
                modalRoot.SetActive(_isOpen);
                return;
            }

            _isOpen = isOpen;
            modalRoot.SetActive(_isOpen);
            _navigationHeld = false;

            if (_isOpen)
            {
                RefreshInventory(true);
            }

            Toggled?.Invoke(_isOpen);
        }

        private void EnsureBuilt()
        {
            if (canvas == null)
            {
                Transform existing = transform.Find("Inventory Canvas");
                canvas = existing != null ? existing.GetComponent<Canvas>() : null;
            }

            if (canvas == null)
            {
                canvas = RuntimeUIFactory.CreateOverlayCanvas(transform, "Inventory Canvas", 30);
            }

            if (modalRoot == null)
            {
                Transform existing = canvas.transform.Find("Inventory Modal");
                RectTransform root = existing as RectTransform;
                if (root == null)
                {
                    root = RuntimeUIFactory.CreateRect(
                        canvas.transform,
                        "Inventory Modal",
                        Vector2.zero,
                        Vector2.one,
                        new Vector2(0.5f, 0.5f),
                        Vector2.zero,
                        Vector2.zero);
                }

                Image dim = root.GetComponent<Image>();
                if (dim == null)
                {
                    dim = root.gameObject.AddComponent<Image>();
                }
                dim.color = new Color(0f, 0f, 0f, 0.7f);
                dim.raycastTarget = false;
                modalRoot = root.gameObject;
            }

            RectTransform panel = GetOrCreateRect(modalRoot.transform, "Panel", new Vector2(590f, 300f));
            Image panelImage = panel.GetComponent<Image>();
            if (panelImage == null)
            {
                panelImage = panel.gameObject.AddComponent<Image>();
            }
            panelImage.color = new Color(0.09f, 0.09f, 0.082f, 0.99f);
            panelImage.raycastTarget = false;

            Image border = CreatePanelImage(panel, "Border", Ink, new Vector2(586f, 296f));
            border.transform.SetAsFirstSibling();

            Image header = CreatePanelImage(panel, "Header", new Color(0.14f, 0.135f, 0.12f, 1f), new Vector2(586f, 38f));
            SetAnchored(header.rectTransform, new Vector2(0f, -2f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

            titleText = GetOrCreateText(
                header.transform,
                "Title",
                new Vector2(250f, 26f),
                new Vector2(18f, -6f),
                new Vector2(0f, 1f),
                20,
                Paper,
                TextAnchor.MiddleLeft,
                "CASE FILE / INVENTORY");

            closeHitArea = GetOrCreateRect(header.transform, "Close", new Vector2(110f, 27f));
            SetAnchored(closeHitArea, new Vector2(-14f, -5f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeText = GetOrCreateText(
                closeHitArea,
                "Close Label",
                new Vector2(110f, 24f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                12,
                DullPaper,
                TextAnchor.MiddleRight,
                "[ESC] CLOSE");

            RectTransform gridArea = GetOrCreateRect(panel, "Slots", new Vector2(254f, 218f));
            SetAnchored(gridArea, new Vector2(-143f, -10f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

            RectTransform detailPanel = GetOrCreateRect(panel, "Selected Item", new Vector2(270f, 218f));
            SetAnchored(detailPanel, new Vector2(146f, -10f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            Image detailBackground = detailPanel.GetComponent<Image>();
            if (detailBackground == null)
            {
                detailBackground = detailPanel.gameObject.AddComponent<Image>();
            }
            detailBackground.color = new Color(0.045f, 0.048f, 0.046f, 1f);
            detailBackground.raycastTarget = false;

            detailsRoot = detailPanel.gameObject;
            Image detailRule = CreatePanelImage(detailPanel, "Top Rule", Rust, new Vector2(270f, 2f));
            SetAnchored(detailRule.rectTransform, new Vector2(0f, -1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

            detailIconImage = CreateImageAt(detailPanel, "Icon", new Color(0.15f, 0.15f, 0.135f, 1f), new Vector2(60f, 60f), new Vector2(-90f, 57f));
            detailIconImage.preserveAspect = true;
            detailIconGlyph = CreateTextAt(
                detailPanel,
                "Icon Glyph",
                new Vector2(54f, 54f),
                new Vector2(-90f, 57f),
                18,
                DullPaper,
                TextAnchor.MiddleCenter,
                "--");

            detailNameText = CreateTextAt(
                detailPanel,
                "Item Name",
                new Vector2(166f, 44f),
                new Vector2(22f, 63f),
                18,
                Paper,
                TextAnchor.MiddleLeft,
                "NO ITEM SELECTED");
            detailTypeText = CreateTextAt(
                detailPanel,
                "Item Type",
                new Vector2(230f, 24f),
                new Vector2(0f, 18f),
                12,
                DullPaper,
                TextAnchor.MiddleLeft,
                "");
            detailDescriptionText = CreateTextAt(
                detailPanel,
                "Item Description",
                new Vector2(234f, 72f),
                new Vector2(0f, -38f),
                14,
                Paper,
                TextAnchor.UpperLeft,
                "Nothing in the case file yet.");
            detailQuantityText = CreateTextAt(
                detailPanel,
                "Item Quantity",
                new Vector2(230f, 22f),
                new Vector2(0f, -94f),
                12,
                DullPaper,
                TextAnchor.MiddleLeft,
                "");

            capacityText = CreateTextAt(
                panel,
                "Capacity Hint",
                new Vector2(275f, 22f),
                new Vector2(-276f, -276f),
                12,
                DullPaper,
                TextAnchor.MiddleLeft,
                "SLOTS 00/16");
            controlsText = CreateTextAt(
                panel,
                "Navigation Hint",
                new Vector2(285f, 22f),
                new Vector2(276f, -276f),
                12,
                DullPaper,
                TextAnchor.MiddleRight,
                "[ARROWS] SELECT   [ESC] CLOSE");

            int capacity = inventory != null ? inventory.Capacity : 16;
            EnsureSlots(Mathf.Max(1, capacity));
            LayoutSlots(Mathf.Max(1, capacity), gridArea);

            if (titleText == null || closeText == null || detailsRoot == null)
            {
                throw new InvalidOperationException("Inventory UI construction failed to create required elements.");
            }

            modalRoot.SetActive(_isOpen);
        }

        private void EnsureSlots(int capacity)
        {
            RectTransform grid = modalRoot.transform.Find("Panel/Slots") as RectTransform;
            if (grid == null)
            {
                return;
            }

            while (slotRoots.Count < capacity)
            {
                int index = slotRoots.Count;
                RectTransform frameRect = RuntimeUIFactory.CreateRect(
                    grid,
                    $"Slot {index + 1:00}",
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    new Vector2(44f, 44f));
                Image frame = frameRect.gameObject.AddComponent<Image>();
                frame.color = new Color(0.34f, 0.32f, 0.28f, 1f);
                frame.raycastTarget = false;

                Image background = CreateImageAt(frameRect, "Slot Background", new Color(0.055f, 0.058f, 0.055f, 1f), new Vector2(40f, 40f), Vector2.zero);
                Image icon = CreateImageAt(frameRect, "Item Icon", Color.white, new Vector2(31f, 29f), new Vector2(0f, 3f));
                icon.preserveAspect = true;
                Text glyph = CreateTextAt(frameRect, "Item Glyph", new Vector2(36f, 24f), new Vector2(0f, 4f), 12, DullPaper, TextAnchor.MiddleCenter, "");
                Text quantity = CreateTextAt(frameRect, "Quantity", new Vector2(37f, 13f), new Vector2(0f, -14f), 9, Paper, TextAnchor.LowerRight, "");

                slotRoots.Add(frameRect);
                slotFrames.Add(frame);
                slotBackgrounds.Add(background);
                slotIcons.Add(icon);
                slotGlyphs.Add(glyph);
                slotQuantities.Add(quantity);
            }
        }

        private void LayoutSlots(int capacity, RectTransform grid)
        {
            _slotCapacity = Mathf.Max(1, capacity);
            _columns = Mathf.CeilToInt(Mathf.Sqrt(_slotCapacity));
            int rows = Mathf.CeilToInt(_slotCapacity / (float)_columns);
            float step = Mathf.Min(56f, 246f / _columns, 206f / rows);
            float slotSize = step * 0.84f;
            float startX = -_columns * step * 0.5f + step * 0.5f;
            float startY = rows * step * 0.5f - step * 0.5f;

            for (int i = 0; i < slotRoots.Count; i++)
            {
                bool visible = i < _slotCapacity;
                slotRoots[i].gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                int column = i % _columns;
                int row = i / _columns;
                slotRoots[i].anchoredPosition = new Vector2(startX + column * step, startY - row * step);
                slotRoots[i].sizeDelta = new Vector2(slotSize, slotSize);
                slotBackgrounds[i].rectTransform.sizeDelta = new Vector2(slotSize - 4f, slotSize - 4f);
                slotIcons[i].rectTransform.sizeDelta = new Vector2(slotSize * 0.68f, slotSize * 0.60f);
                slotIcons[i].rectTransform.anchoredPosition = new Vector2(0f, slotSize * 0.06f);
                slotGlyphs[i].rectTransform.sizeDelta = new Vector2(slotSize * 0.8f, slotSize * 0.58f);
                slotGlyphs[i].rectTransform.anchoredPosition = new Vector2(0f, slotSize * 0.06f);
                slotGlyphs[i].fontSize = _slotCapacity > 24 ? 9 : 12;
                slotQuantities[i].rectTransform.sizeDelta = new Vector2(slotSize - 7f, 12f);
                slotQuantities[i].rectTransform.anchoredPosition = new Vector2(0f, -slotSize * 0.32f);
            }
        }

        private void HandleNavigation(Vector2 navigation)
        {
            Vector2Int direction = GetNavigationDirection(navigation);
            if (direction == Vector2Int.zero)
            {
                _navigationHeld = false;
                return;
            }

            if (!_navigationHeld || direction != _navigationDirection)
            {
                MoveSelection(direction);
                _navigationHeld = true;
                _navigationDirection = direction;
                _nextNavigationTime = Time.unscaledTime + 0.32f;
            }
            else if (Time.unscaledTime >= _nextNavigationTime)
            {
                MoveSelection(direction);
                _nextNavigationTime = Time.unscaledTime + 0.12f;
            }
        }

        private static Vector2Int GetNavigationDirection(Vector2 navigation)
        {
            if (navigation.sqrMagnitude < 0.36f)
            {
                return Vector2Int.zero;
            }

            if (Mathf.Abs(navigation.x) > Mathf.Abs(navigation.y))
            {
                return new Vector2Int(navigation.x > 0f ? 1 : -1, 0);
            }

            return new Vector2Int(0, navigation.y > 0f ? -1 : 1);
        }

        private void MoveSelection(Vector2Int direction)
        {
            if (_items.Count == 0)
            {
                return;
            }

            if (_selectedIndex < 0)
            {
                SelectItem(0);
                return;
            }

            int row = _selectedIndex / _columns;
            int column = _selectedIndex % _columns;
            int rowCount = Mathf.CeilToInt(_items.Count / (float)_columns);
            int targetRow = Mathf.Clamp(row + direction.y, 0, rowCount - 1);
            int targetColumn = Mathf.Clamp(column + direction.x, 0, _columns - 1);
            int next = targetRow * _columns + targetColumn;
            SelectItem(Mathf.Min(next, _items.Count - 1));
        }

        private void HandlePointer()
        {
            if (!input.PointerPressed)
            {
                return;
            }

            Vector2 pointer = input.PointerPosition;
            if (closeHitArea != null && RectTransformUtility.RectangleContainsScreenPoint(closeHitArea, pointer, null))
            {
                SetOpen(false);
                return;
            }

            for (int i = 0; i < _items.Count && i < slotRoots.Count; i++)
            {
                if (slotRoots[i].gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(slotRoots[i], pointer, null))
                {
                    SelectItem(i);
                    return;
                }
            }
        }

        private void SelectItem(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                _selectedIndex = -1;
            }
            else
            {
                _selectedIndex = index;
            }

            RefreshSlots();
            RefreshDetails();
        }

        private void SubscribeToInventory()
        {
            if (isActiveAndEnabled && inventory != null)
            {
                inventory.Changed -= OnInventoryChanged;
                inventory.Changed += OnInventoryChanged;
            }
        }

        private void UnsubscribeFromInventory()
        {
            if (inventory != null)
            {
                inventory.Changed -= OnInventoryChanged;
            }
        }

        private void OnInventoryChanged()
        {
            RefreshInventory(true);
        }

        private void RefreshInventory(bool preserveSelection)
        {
            string selectedId = null;
            int previousIndex = _selectedIndex;
            if (preserveSelection && _selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                ItemData selected = _items[_selectedIndex].Data;
                selectedId = selected != null ? selected.Id : null;
            }

            _items.Clear();
            if (inventory != null)
            {
                IReadOnlyList<InventoryItem> contents = inventory.Items;
                for (int i = 0; i < contents.Count; i++)
                {
                    if (contents[i] != null && contents[i].Data != null)
                    {
                        _items.Add(contents[i]);
                    }
                }
            }

            if (_items.Count == 0)
            {
                _selectedIndex = -1;
            }
            else if (selectedId != null)
            {
                _selectedIndex = FindItem(selectedId);
                if (_selectedIndex < 0)
                {
                    _selectedIndex = Mathf.Clamp(previousIndex, 0, _items.Count - 1);
                }
            }
            else
            {
                _selectedIndex = Mathf.Clamp(previousIndex, 0, _items.Count - 1);
            }

            int capacity = inventory != null ? Mathf.Max(1, inventory.Capacity) : 16;
            if (capacity != _slotCapacity)
            {
                RectTransform grid = modalRoot.transform.Find("Panel/Slots") as RectTransform;
                EnsureSlots(capacity);
                if (grid != null)
                {
                    LayoutSlots(capacity, grid);
                }
            }

            RefreshSlots();
            RefreshDetails();
            RefreshCapacityText();
        }

        private int FindItem(string id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Data.Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < slotRoots.Count; i++)
            {
                if (!slotRoots[i].gameObject.activeSelf)
                {
                    continue;
                }

                bool filled = i < _items.Count && _items[i].Data != null;
                bool selected = filled && i == _selectedIndex;
                ItemData data = filled ? _items[i].Data : null;

                slotFrames[i].color = selected
                    ? Rust
                    : filled ? new Color(0.48f, 0.45f, 0.38f, 1f) : new Color(0.24f, 0.24f, 0.21f, 1f);
                slotBackgrounds[i].color = selected
                    ? new Color(0.13f, 0.09f, 0.075f, 1f)
                    : filled ? new Color(0.065f, 0.068f, 0.062f, 1f) : new Color(0.048f, 0.051f, 0.048f, 1f);

                Sprite icon = data != null ? data.Icon : null;
                slotIcons[i].sprite = icon;
                slotIcons[i].enabled = icon != null;
                slotGlyphs[i].gameObject.SetActive(filled && icon == null);
                slotGlyphs[i].text = filled ? Abbreviation(data.DisplayName) : string.Empty;
                slotQuantities[i].text = filled && _items[i].Quantity > 1 ? $"x{_items[i].Quantity}" : string.Empty;
                slotQuantities[i].gameObject.SetActive(filled && _items[i].Quantity > 1);
            }
        }

        private void RefreshDetails()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _items.Count)
            {
                detailNameText.text = "NO ITEM SELECTED";
                detailTypeText.text = "CASE FILE";
                detailDescriptionText.text = "Nothing in the case file yet.";
                detailQuantityText.text = string.Empty;
                detailIconImage.sprite = null;
                detailIconGlyph.gameObject.SetActive(true);
                detailIconGlyph.text = "--";
                return;
            }

            InventoryItem entry = _items[_selectedIndex];
            ItemData data = entry.Data;
            detailNameText.text = string.IsNullOrWhiteSpace(data.DisplayName) ? "UNTITLED ITEM" : data.DisplayName.ToUpperInvariant();
            detailTypeText.text = data.Type.ToString().ToUpperInvariant();
            detailDescriptionText.text = string.IsNullOrWhiteSpace(data.Description) ? "No description recorded." : data.Description;
            detailQuantityText.text = $"QUANTITY  {entry.Quantity}";
            detailIconImage.sprite = data.Icon;
            detailIconImage.enabled = true;
            detailIconImage.color = data.Icon != null
                ? Color.white
                : new Color(0.15f, 0.15f, 0.135f, 1f);
            detailIconGlyph.gameObject.SetActive(data.Icon == null);
            detailIconGlyph.text = Abbreviation(data.DisplayName);
        }

        private void RefreshCapacityText()
        {
            int capacity = inventory != null ? inventory.Capacity : 16;
            int used = _items.Count;
            capacityText.text = inventory == null
                ? "INVENTORY UNAVAILABLE"
                : used >= capacity ? $"SLOTS {used:00}/{capacity:00}  /  FULL" : $"SLOTS {used:00}/{capacity:00}  /  AVAILABLE";
            capacityText.color = used >= capacity && inventory != null ? Rust : DullPaper;
        }

        private void RefreshControlsText()
        {
            controlsText.text = input != null && input.UsingGamepad
                ? "[STICK / D-PAD] SELECT   [B] CLOSE"
                : "[ARROWS] SELECT   [ESC] CLOSE";
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

            for (int i = 0; i < slotGlyphs.Count; i++)
            {
                if (slotGlyphs[i] != null)
                {
                    slotGlyphs[i].font = resolvedFont;
                }

                if (slotQuantities[i] != null)
                {
                    slotQuantities[i].font = resolvedFont;
                }
            }
        }

        private Image CreatePanelImage(Transform parent, string name, Color color, Vector2 size)
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
            rect.sizeDelta = size;

            Image image = rect.GetComponent<Image>();
            if (image == null)
            {
                image = rect.gameObject.AddComponent<Image>();
            }
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image CreateImageAt(Transform parent, string name, Color color, Vector2 size, Vector2 position)
        {
            Transform existing = parent.Find(name);
            Image image = existing != null ? existing.GetComponent<Image>() : null;
            if (image == null)
            {
                image = RuntimeUIFactory.CreateImage(parent, name, color);
            }
            image.color = color;
            image.raycastTarget = false;
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            return image;
        }

        private Text CreateTextAt(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 position,
            int fontSize,
            Color color,
            TextAnchor alignment,
            string value)
        {
            return GetOrCreateText(
                parent,
                name,
                size,
                position,
                new Vector2(0.5f, 0.5f),
                fontSize,
                color,
                alignment,
                value);
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

            text.rectTransform.anchorMin = new Vector2(pivot.x == 0f ? 0f : pivot.x, pivot.y == 1f ? 1f : pivot.y);
            text.rectTransform.anchorMax = text.rectTransform.anchorMin;
            text.rectTransform.pivot = pivot;
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            if (!_texts.Contains(text))
            {
                _texts.Add(text);
            }
            return text;
        }

        private RectTransform GetOrCreateRect(Transform parent, string name, Vector2 size)
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

        private static string Abbreviation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "??";
            }

            string[] words = value.Trim().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
            }

            string compact = value.Trim();
            return compact.Length > 3 ? compact.Substring(0, 3).ToUpperInvariant() : compact.ToUpperInvariant();
        }
    }
}
