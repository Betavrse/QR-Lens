using System;
using System.Collections.Generic;
using QRLens.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QRLens.UI
{
    public sealed class QRPanel : MonoBehaviour
    {
        private const int MaxDisplayedPayloadLength = 2048;
        private const float TransitionDuration = 0.22f;

        private static readonly Vector2 ScanningCardSize = new Vector2(650f, 200f);
        private static readonly Vector2 ResultCardSize = new Vector2(840f, 560f);
        private static readonly Vector2 ErrorCardSize = new Vector2(740f, 410f);

        private static readonly Color SurfaceTop = Hex("18243A", 0.97f);
        private static readonly Color SurfaceBottom = Hex("09101D", 0.985f);
        private static readonly Color SurfaceBorder = Hex("6B82AA", 0.34f);
        private static readonly Color InnerSurfaceTop = Hex("1A2942", 0.92f);
        private static readonly Color InnerSurfaceBottom = Hex("101A2B", 0.94f);
        private static readonly Color PrimaryTop = Hex("2778FF");
        private static readonly Color PrimaryBottom = Hex("0E47DB");
        private static readonly Color AccentBlue = Hex("4C91FF");
        private static readonly Color AccentMint = Hex("38E0B5");
        private static readonly Color AccentCoral = Hex("FF7A7A");
        private static readonly Color TextPrimary = Hex("F7FAFF");
        private static readonly Color TextSecondary = Hex("AEBBD0");
        private static readonly Color TextMuted = Hex("7F90AA");

        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
        private readonly Dictionary<Button, ButtonVisual> _buttonVisuals = new Dictionary<Button, ButtonVisual>();

        private Transform _head;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private EventSystem _eventSystem;
        private RectTransform _card;
        private RectTransform _shadow;
        private CanvasGroup _cardGroup;
        private RoundedRectGraphic _cardSurface;

        private RectTransform _scanningView;
        private Text _scanTitle;
        private Text _scanMessage;
        private CanvasGroup _scanIndicatorGroup;
        private RectTransform _scanIndicator;
        private RectTransform _scanSweep;

        private RectTransform _resultView;
        private RoundedRectGraphic _statusPill;
        private RoundedRectGraphic _statusDot;
        private Text _statusText;
        private RoundedRectGraphic _resultGlyphSurface;
        private Text _resultGlyph;
        private Text _contentLabel;
        private Text _payload;
        private Text _hint;
        private Button _openButton;
        private Button _dismissButton;

        private RectTransform _errorView;
        private RoundedRectGraphic _errorGlyphSurface;
        private Text _errorMessage;

        private RectTransform _cursor;
        private RoundedRectGraphic _cursorOuter;
        private Button _hoveredButton;
        private bool _hasResult;
        private PanelMode _mode;
        private float _transitionStartedAt;
        private Vector2 _transitionFromSize;
        private Vector2 _targetCardSize;

        public event Action OpenRequested;

        public event Action DismissRequested;

        public static QRPanel Create(Transform head)
        {
            var root = new GameObject("QR Lens Panel", typeof(RectTransform));
            var panel = root.AddComponent<QRPanel>();
            panel.Build(head);
            return panel;
        }

        public void ShowScanning(string message)
        {
            _hasResult = false;
            SetMode(PanelMode.Scanning);

            var normalized = string.IsNullOrWhiteSpace(message) ? "Scanning…" : message.Trim();
            if (normalized.IndexOf("Opening", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _scanTitle.text = "Opening in your browser";
                _scanMessage.text = "QR Lens will be ready when you return";
            }
            else if (normalized.IndexOf("Starting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     normalized.IndexOf("Preparing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _scanTitle.text = "Starting the camera";
                _scanMessage.text = "Everything is processed privately on this headset";
            }
            else
            {
                _scanTitle.text = "Looking for a QR code";
                _scanMessage.text = "Keep the code comfortably inside your view";
            }

            SetHoveredButton(null, null);
            SetCursorVisible(false);
            PlaceInFrontOfUser();
        }

        public void ShowResult(QRResult result)
        {
            _hasResult = true;
            SetMode(PanelMode.Result);

            var payload = result.Payload ?? string.Empty;
            var hasUrl = result.HasTextPayload && URLLauncher.TryGetHttpUrl(payload, out _);
            var statusColor = hasUrl ? AccentMint : AccentBlue;

            _statusText.text = hasUrl ? "LINK READY" : "TEXT FOUND";
            _statusText.color = statusColor;
            _statusPill.Configure(statusColor.WithAlpha(0.13f), statusColor.WithAlpha(0.08f), 18f, 1f, statusColor.WithAlpha(0.38f));
            _statusDot.Configure(statusColor, statusColor, 6f);
            _resultGlyphSurface.Configure(statusColor.WithAlpha(0.18f), statusColor.WithAlpha(0.1f), 28f, 1f, statusColor.WithAlpha(0.42f));
            _resultGlyph.text = hasUrl ? "↗" : "Aa";
            _resultGlyph.color = statusColor;

            _contentLabel.text = hasUrl ? "WEB LINK" : "QR CONTENT";
            _payload.text = FormatPayload(payload, result.HasTextPayload);
            _payload.color = TextPrimary;
            _openButton.gameObject.SetActive(hasUrl);
            _dismissButton.gameObject.SetActive(true);

            var dismissVisual = _buttonVisuals[_dismissButton];
            if (hasUrl)
            {
                _buttonVisuals[_openButton].Rect.anchoredPosition = new Vector2(-177f, -153f);
                dismissVisual.Rect.anchoredPosition = new Vector2(177f, -153f);
                dismissVisual.Rect.sizeDelta = new Vector2(330f, 76f);
                _hint.text = "Aim and press trigger or A  •  Press B to scan again";
            }
            else
            {
                dismissVisual.Rect.anchoredPosition = new Vector2(0f, -153f);
                dismissVisual.Rect.sizeDelta = new Vector2(360f, 76f);
                _hint.text = "This content is shown only on your headset";
            }

            SetHoveredButton(null, null);
            SetCursorVisible(false);
            PlaceInFrontOfUser();
        }

        public void ShowError(string message)
        {
            _hasResult = false;
            SetMode(PanelMode.Error);
            _errorMessage.text = string.IsNullOrWhiteSpace(message)
                ? "QR scanning could not start."
                : message.Trim();
            _errorGlyphSurface.Configure(
                AccentCoral.WithAlpha(0.18f),
                AccentCoral.WithAlpha(0.1f),
                34f,
                1f,
                AccentCoral.WithAlpha(0.45f));
            SetHoveredButton(null, null);
            SetCursorVisible(false);
            PlaceInFrontOfUser();
        }

        public void ProcessPointer(bool selectPressed)
        {
            if (!_hasResult || !_canvas || !_eventSystem)
            {
                SetHoveredButton(null, null);
                SetCursorVisible(false);
                return;
            }

            ProcessScreenPointer(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), selectPressed);
        }

        public void ProcessPointer(Ray worldRay, bool selectPressed)
        {
            if (!_hasResult || !_canvas || !_eventSystem || !_canvas.worldCamera)
            {
                SetHoveredButton(null, null);
                SetCursorVisible(false);
                return;
            }

            var panelPlane = new Plane(transform.forward, transform.position);
            if (!panelPlane.Raycast(worldRay, out var distance) || distance <= 0f)
            {
                SetHoveredButton(null, null);
                SetCursorVisible(false);
                return;
            }

            var screenPoint = _canvas.worldCamera.WorldToScreenPoint(worldRay.GetPoint(distance));
            if (screenPoint.z <= 0f)
            {
                SetHoveredButton(null, null);
                SetCursorVisible(false);
                return;
            }

            ProcessScreenPointer(screenPoint, selectPressed);
        }

        private void ProcessScreenPointer(Vector2 screenPosition, bool selectPressed)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(_card, screenPosition, _canvas.worldCamera))
            {
                SetHoveredButton(null, null);
                SetCursorVisible(false);
                return;
            }

            var pointer = new PointerEventData(_eventSystem) { position = screenPosition };
            UpdateCursor(screenPosition);
            _raycastResults.Clear();
            _raycaster.Raycast(pointer, _raycastResults);

            Button button = null;
            foreach (var result in _raycastResults)
            {
                button = result.gameObject.GetComponentInParent<Button>();
                if (button && button.isActiveAndEnabled && button.interactable)
                {
                    break;
                }
            }

            SetHoveredButton(button, pointer);
            if (selectPressed && button)
            {
                ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            }
        }

        private void Build(Transform head)
        {
            _head = head;
            _eventSystem = FindFirstObjectByType<EventSystem>();
            if (!_eventSystem)
            {
                _eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
            }

            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(880f, 600f);
            root.localScale = Vector3.one * 0.00105f;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = head.GetComponent<Camera>();
            _canvas.sortingOrder = 10;
            _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            _shadow = CreateRect("Card Shadow", transform, new Vector2(0f, -13f), ScanningCardSize + new Vector2(16f, 18f));
            var shadowSurface = _shadow.gameObject.AddComponent<RoundedRectGraphic>();
            shadowSurface.Configure(Color.black.WithAlpha(0.38f), Color.black.WithAlpha(0.58f), 48f);
            shadowSurface.raycastTarget = false;

            _card = CreateRect("Card", transform, Vector2.zero, ScanningCardSize);
            _cardSurface = _card.gameObject.AddComponent<RoundedRectGraphic>();
            _cardSurface.Configure(SurfaceTop, SurfaceBottom, 42f, 1.5f, SurfaceBorder);
            _cardSurface.raycastTarget = false;
            _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();

            BuildScanningView();
            BuildResultView();
            BuildErrorView();
            BuildPointerCursor();
            ShowScanning("Scanning…");
        }

        private void BuildScanningView()
        {
            _scanningView = CreateRect("Scanning View", _card, Vector2.zero, ScanningCardSize);
            CreateLogo(_scanningView, new Vector2(-255f, 25f), 66f);

            _scanTitle = CreateText(
                "Scanning Title",
                _scanningView,
                new Vector2(-15f, 48f),
                new Vector2(380f, 40f),
                29,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextPrimary);
            _scanMessage = CreateText(
                "Scanning Message",
                _scanningView,
                new Vector2(-5f, 8f),
                new Vector2(400f, 32f),
                20,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                TextSecondary);

            var privacyDot = CreateRoundedRect(
                "Privacy Dot",
                _scanningView,
                new Vector2(-215f, -47f),
                new Vector2(9f, 9f),
                AccentMint,
                AccentMint,
                5f);
            privacyDot.raycastTarget = false;
            CreateText(
                "Privacy Label",
                _scanningView,
                new Vector2(-92f, -47f),
                new Vector2(215f, 26f),
                15,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMuted).text = "PRIVATE  •  ON-DEVICE";

            _scanIndicator = CreateRect("Scan Indicator", _scanningView, new Vector2(250f, 4f), new Vector2(104f, 104f));
            _scanIndicatorGroup = _scanIndicator.gameObject.AddComponent<CanvasGroup>();
            CreateRoundedRect(
                "Indicator Glow",
                _scanIndicator,
                Vector2.zero,
                new Vector2(102f, 102f),
                AccentBlue.WithAlpha(0.13f),
                AccentBlue.WithAlpha(0.045f),
                51f,
                1f,
                AccentBlue.WithAlpha(0.25f));

            CreateScannerCorner(_scanIndicator, new Vector2(-25f, 28f), true, true);
            CreateScannerCorner(_scanIndicator, new Vector2(25f, 28f), false, true);
            CreateScannerCorner(_scanIndicator, new Vector2(-25f, -28f), true, false);
            CreateScannerCorner(_scanIndicator, new Vector2(25f, -28f), false, false);

            var sweep = CreateRoundedRect(
                "Scan Sweep",
                _scanIndicator,
                Vector2.zero,
                new Vector2(58f, 4f),
                AccentMint,
                AccentBlue,
                2f);
            _scanSweep = (RectTransform)sweep.transform;
        }

        private void BuildResultView()
        {
            _resultView = CreateRect("Result View", _card, Vector2.zero, ResultCardSize);
            CreateLogo(_resultView, new Vector2(-350f, 222f), 58f);
            CreateText(
                "Brand",
                _resultView,
                new Vector2(-155f, 234f),
                new Vector2(300f, 34f),
                27,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextPrimary).text = "QR Lens";
            CreateText(
                "Brand Subtitle",
                _resultView,
                new Vector2(-120f, 204f),
                new Vector2(370f, 25f),
                16,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                TextMuted).text = "A quiet scanner for spatial computing";

            var privacyPill = CreateRoundedRect(
                "Privacy Pill",
                _resultView,
                new Vector2(305f, 220f),
                new Vector2(170f, 38f),
                AccentMint.WithAlpha(0.1f),
                AccentMint.WithAlpha(0.06f),
                19f,
                1f,
                AccentMint.WithAlpha(0.26f));
            privacyPill.raycastTarget = false;
            var privacyDot = CreateRoundedRect(
                "Privacy Dot",
                privacyPill.transform,
                new Vector2(-59f, 0f),
                new Vector2(8f, 8f),
                AccentMint,
                AccentMint,
                4f);
            privacyDot.raycastTarget = false;
            CreateText(
                "Privacy Text",
                privacyPill.transform,
                new Vector2(11f, 0f),
                new Vector2(122f, 26f),
                14,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                AccentMint).text = "ON-DEVICE";

            var divider = CreateRoundedRect(
                "Header Divider",
                _resultView,
                new Vector2(0f, 170f),
                new Vector2(740f, 1.5f),
                Hex("93A8C8", 0.19f),
                Hex("93A8C8", 0.08f),
                1f);
            divider.raycastTarget = false;

            _statusPill = CreateRoundedRect(
                "Result Status",
                _resultView,
                new Vector2(-281f, 125f),
                new Vector2(178f, 38f),
                AccentMint.WithAlpha(0.13f),
                AccentMint.WithAlpha(0.08f),
                19f,
                1f,
                AccentMint.WithAlpha(0.38f));
            _statusPill.raycastTarget = false;
            _statusDot = CreateRoundedRect(
                "Status Dot",
                _statusPill.transform,
                new Vector2(-61f, 0f),
                new Vector2(10f, 10f),
                AccentMint,
                AccentMint,
                5f);
            _statusDot.raycastTarget = false;
            _statusText = CreateText(
                "Status Label",
                _statusPill.transform,
                new Vector2(13f, 0f),
                new Vector2(128f, 28f),
                15,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                AccentMint);

            _resultGlyphSurface = CreateRoundedRect(
                "Result Glyph Surface",
                _resultView,
                new Vector2(326f, 125f),
                new Vector2(56f, 56f),
                AccentMint.WithAlpha(0.18f),
                AccentMint.WithAlpha(0.1f),
                28f);
            _resultGlyphSurface.raycastTarget = false;
            _resultGlyph = CreateText(
                "Result Glyph",
                _resultGlyphSurface.transform,
                Vector2.zero,
                new Vector2(50f, 50f),
                27,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                AccentMint);

            var contentSurface = CreateRoundedRect(
                "Payload Surface",
                _resultView,
                new Vector2(0f, 12f),
                new Vector2(740f, 178f),
                InnerSurfaceTop,
                InnerSurfaceBottom,
                24f,
                1f,
                Hex("7B94BA", 0.23f));
            contentSurface.raycastTarget = false;
            _contentLabel = CreateText(
                "Content Type",
                contentSurface.transform,
                new Vector2(-303f, 57f),
                new Vector2(110f, 24f),
                14,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextMuted);
            _payload = CreateText(
                "Payload",
                contentSurface.transform,
                new Vector2(0f, -16f),
                new Vector2(650f, 112f),
                30,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                TextPrimary,
                true);

            _openButton = CreateButton(
                "Open Link",
                "Open in Browser",
                "↗",
                new Vector2(-177f, -153f),
                PrimaryTop,
                PrimaryBottom,
                AccentBlue.WithAlpha(0.78f));
            _openButton.onClick.AddListener(() => OpenRequested?.Invoke());

            _dismissButton = CreateButton(
                "Dismiss / Scan Again",
                "Scan Again",
                "↻",
                new Vector2(177f, -153f),
                Hex("263650"),
                Hex("18263C"),
                Hex("91A9CC", 0.42f));
            _dismissButton.onClick.AddListener(() => DismissRequested?.Invoke());

            _hint = CreateText(
                "Interaction Hint",
                _resultView,
                new Vector2(0f, -225f),
                new Vector2(700f, 34f),
                17,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                TextMuted);
        }

        private void BuildErrorView()
        {
            _errorView = CreateRect("Error View", _card, Vector2.zero, ErrorCardSize);
            CreateLogo(_errorView, new Vector2(-300f, 155f), 52f);
            CreateText(
                "Error Brand",
                _errorView,
                new Vector2(-175f, 155f),
                new Vector2(180f, 34f),
                25,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                TextPrimary).text = "QR Lens";

            _errorGlyphSurface = CreateRoundedRect(
                "Error Glyph Surface",
                _errorView,
                new Vector2(0f, 62f),
                new Vector2(68f, 68f),
                AccentCoral.WithAlpha(0.18f),
                AccentCoral.WithAlpha(0.1f),
                34f,
                1f,
                AccentCoral.WithAlpha(0.45f));
            _errorGlyphSurface.raycastTarget = false;
            CreateText(
                "Error Glyph",
                _errorGlyphSurface.transform,
                Vector2.zero,
                new Vector2(60f, 60f),
                31,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                AccentCoral).text = "!";
            CreateText(
                "Error Title",
                _errorView,
                new Vector2(0f, 5f),
                new Vector2(580f, 40f),
                29,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextPrimary).text = "Scanner needs attention";
            _errorMessage = CreateText(
                "Error Message",
                _errorView,
                new Vector2(0f, -54f),
                new Vector2(590f, 70f),
                21,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                TextSecondary,
                true);
            CreateText(
                "Error Hint",
                _errorView,
                new Vector2(0f, -132f),
                new Vector2(600f, 30f),
                16,
                FontStyle.Normal,
                TextAnchor.MiddleCenter,
                TextMuted).text = "Check permission in Quest Settings, then reopen QR Lens";
        }

        private void BuildPointerCursor()
        {
            _cursor = CreateRect("Pointer Cursor", _resultView, Vector2.zero, new Vector2(28f, 28f));
            _cursor.SetAsLastSibling();
            _cursorOuter = _cursor.gameObject.AddComponent<RoundedRectGraphic>();
            _cursorOuter.Configure(Color.white.WithAlpha(0.12f), Color.white.WithAlpha(0.06f), 14f, 2f, Color.white.WithAlpha(0.88f));
            _cursorOuter.raycastTarget = false;
            var dot = CreateRoundedRect(
                "Cursor Dot",
                _cursor,
                Vector2.zero,
                new Vector2(6f, 6f),
                Color.white,
                Color.white,
                3f);
            dot.raycastTarget = false;
            _cursor.gameObject.SetActive(false);
        }

        private void Update()
        {
            var elapsed = Time.unscaledTime - _transitionStartedAt;
            var interpolation = Mathf.Clamp01(elapsed / TransitionDuration);
            var eased = 1f - Mathf.Pow(1f - interpolation, 3f);
            var cardSize = Vector2.Lerp(_transitionFromSize, _targetCardSize, eased);
            _card.sizeDelta = cardSize;
            _shadow.sizeDelta = cardSize + new Vector2(16f, 18f);
            _card.localScale = Vector3.one * Mathf.Lerp(0.975f, 1f, eased);
            _cardGroup.alpha = Mathf.Lerp(0.35f, 1f, eased);

            foreach (var visual in _buttonVisuals.Values)
            {
                visual.Tick(Time.unscaledDeltaTime);
            }

            if (_mode == PanelMode.Scanning && _scanIndicator)
            {
                var scanPhase = Mathf.PingPong(Time.unscaledTime * 0.72f, 1f);
                _scanSweep.anchoredPosition = new Vector2(0f, Mathf.Lerp(25f, -25f, scanPhase));
                var pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.8f) * 0.018f;
                _scanIndicator.localScale = Vector3.one * pulse;
                _scanIndicatorGroup.alpha = 0.82f + Mathf.Sin(Time.unscaledTime * 2.8f) * 0.12f;
            }
        }

        private void LateUpdate()
        {
            if (!_head)
            {
                return;
            }

            var directionToPanel = transform.position - _head.position;
            if (directionToPanel.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(directionToPanel.normalized, Vector3.up);
            }
        }

        private void SetMode(PanelMode mode)
        {
            _mode = mode;
            _scanningView.gameObject.SetActive(mode == PanelMode.Scanning);
            _resultView.gameObject.SetActive(mode == PanelMode.Result);
            _errorView.gameObject.SetActive(mode == PanelMode.Error);

            _transitionFromSize = _card ? _card.sizeDelta : ScanningCardSize;
            _targetCardSize = mode switch
            {
                PanelMode.Result => ResultCardSize,
                PanelMode.Error => ErrorCardSize,
                _ => ScanningCardSize
            };
            _transitionStartedAt = Time.unscaledTime;

            var borderColor = mode switch
            {
                PanelMode.Result => AccentBlue.WithAlpha(0.42f),
                PanelMode.Error => AccentCoral.WithAlpha(0.38f),
                _ => SurfaceBorder
            };
            _cardSurface.Configure(SurfaceTop, SurfaceBottom, 42f, 1.5f, borderColor);
        }

        private void PlaceInFrontOfUser()
        {
            if (!_head)
            {
                return;
            }

            var flatForward = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.5f)
            {
                flatForward = _head.forward;
            }

            var distance = _mode == PanelMode.Scanning ? 1.22f : 1.35f;
            var verticalOffset = _mode == PanelMode.Scanning ? -0.23f : -0.06f;
            transform.position = _head.position + flatForward * distance + Vector3.up * verticalOffset;
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }

        private Button CreateButton(
            string name,
            string label,
            string glyph,
            Vector2 position,
            Color topColor,
            Color bottomColor,
            Color borderColor)
        {
            var buttonRect = CreateRect(name, _resultView, position, new Vector2(330f, 76f));
            var focusRect = CreateRect("Focus Ring", buttonRect, Vector2.zero, buttonRect.sizeDelta + new Vector2(10f, 10f));
            focusRect.SetAsFirstSibling();
            var focus = focusRect.gameObject.AddComponent<RoundedRectGraphic>();
            focus.Configure(AccentBlue.WithAlpha(0.08f), AccentBlue.WithAlpha(0.04f), 25f, 2f, AccentBlue.WithAlpha(0.9f));
            focus.raycastTarget = false;
            var focusGroup = focusRect.gameObject.AddComponent<CanvasGroup>();
            focusGroup.alpha = 0f;

            var surface = buttonRect.gameObject.AddComponent<RoundedRectGraphic>();
            surface.Configure(topColor, bottomColor, 21f, 1f, borderColor);
            surface.raycastTarget = true;

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = surface;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.78f, 0.84f, 0.94f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.07f;
            button.colors = colors;

            CreateText(
                "Label",
                buttonRect,
                new Vector2(-10f, 0f),
                new Vector2(245f, 50f),
                23,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextPrimary).text = label;

            var glyphSurface = CreateRoundedRect(
                "Glyph Surface",
                buttonRect,
                new Vector2(127f, 0f),
                new Vector2(42f, 42f),
                Color.white.WithAlpha(0.13f),
                Color.white.WithAlpha(0.07f),
                21f,
                1f,
                Color.white.WithAlpha(0.17f));
            glyphSurface.raycastTarget = false;
            CreateText(
                "Glyph",
                glyphSurface.transform,
                Vector2.zero,
                new Vector2(38f, 38f),
                22,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                TextPrimary).text = glyph;

            _buttonVisuals.Add(button, new ButtonVisual(buttonRect, focusGroup));
            return button;
        }

        private void CreateLogo(Transform parent, Vector2 position, float size)
        {
            var maskRect = CreateRect("QR Lens Logo", parent, position, new Vector2(size, size));
            var maskGraphic = maskRect.gameObject.AddComponent<RoundedRectGraphic>();
            maskGraphic.Configure(Color.white, Color.white, size * 0.23f, 1f, Color.white.WithAlpha(0.34f));
            maskGraphic.raycastTarget = false;
            var mask = maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var imageRect = CreateRect("Logo Image", maskRect, Vector2.zero, new Vector2(size, size));
            var image = imageRect.gameObject.AddComponent<RawImage>();
            image.texture = Resources.Load<Texture2D>("qrlens-logo");
            image.uvRect = new Rect(0.11f, 0.11f, 0.78f, 0.78f);
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static void CreateScannerCorner(Transform parent, Vector2 position, bool left, bool top)
        {
            var horizontal = CreateRoundedRect(
                "Scanner Corner H",
                parent,
                position + new Vector2(left ? 8f : -8f, 0f),
                new Vector2(23f, 5f),
                AccentBlue,
                AccentBlue,
                2.5f);
            horizontal.raycastTarget = false;
            var vertical = CreateRoundedRect(
                "Scanner Corner V",
                parent,
                position + new Vector2(0f, top ? -8f : 8f),
                new Vector2(5f, 23f),
                AccentBlue,
                AccentBlue,
                2.5f);
            vertical.raycastTarget = false;
        }

        private static RoundedRectGraphic CreateRoundedRect(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            Color topColor,
            Color bottomColor,
            float radius,
            float borderWidth = 0f,
            Color? borderColor = null)
        {
            var rect = CreateRect(name, parent, position, size);
            var graphic = rect.gameObject.AddComponent<RoundedRectGraphic>();
            graphic.Configure(topColor, bottomColor, radius, borderWidth, borderColor);
            return graphic;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            var rect = (RectTransform)rectObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            int fontSize,
            FontStyle style,
            TextAnchor alignment,
            Color color,
            bool resizeToFit = false)
        {
            var textRect = CreateRect(name, parent, position, size);
            var text = textRect.gameObject.AddComponent<Text>();
            text.font = UIFont.Value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = resizeToFit;
            text.resizeTextMinSize = Mathf.Min(18, fontSize);
            text.resizeTextMaxSize = fontSize;
            text.supportRichText = false;
            text.raycastTarget = false;
            return text;
        }

        private void UpdateCursor(Vector2 screenPosition)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _resultView,
                    screenPosition,
                    _canvas.worldCamera,
                    out var localPoint))
            {
                SetCursorVisible(false);
                return;
            }

            _cursor.anchoredPosition = localPoint;
            SetCursorVisible(true);
        }

        private void SetCursorVisible(bool visible)
        {
            if (_cursor && _cursor.gameObject.activeSelf != visible)
            {
                _cursor.gameObject.SetActive(visible);
            }
        }

        private void SetHoveredButton(Button button, PointerEventData pointer)
        {
            if (_hoveredButton == button)
            {
                return;
            }

            if (_hoveredButton)
            {
                _buttonVisuals[_hoveredButton].SetHovered(false);
                if (pointer != null)
                {
                    ExecuteEvents.Execute(_hoveredButton.gameObject, pointer, ExecuteEvents.pointerExitHandler);
                }
            }

            _hoveredButton = button;
            if (_hoveredButton)
            {
                _buttonVisuals[_hoveredButton].SetHovered(true);
                _cursorOuter.Configure(
                    AccentBlue.WithAlpha(0.2f),
                    AccentBlue.WithAlpha(0.1f),
                    14f,
                    2f,
                    Color.white.WithAlpha(0.95f));
                if (pointer != null)
                {
                    ExecuteEvents.Execute(_hoveredButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
                }
            }
            else if (_cursorOuter)
            {
                _cursorOuter.Configure(
                    Color.white.WithAlpha(0.12f),
                    Color.white.WithAlpha(0.06f),
                    14f,
                    2f,
                    Color.white.WithAlpha(0.88f));
            }
        }

        private static string FormatPayload(string payload, bool hasTextPayload)
        {
            if (!hasTextPayload)
            {
                return string.IsNullOrEmpty(payload) ? "Binary QR payload" : payload;
            }

            if (string.IsNullOrEmpty(payload))
            {
                return "This QR code has no readable payload.";
            }

            return payload.Length <= MaxDisplayedPayloadLength
                ? payload
                : payload.Substring(0, MaxDisplayedPayloadLength) + "…";
        }

        private static Color Hex(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString($"#{hex}", out var color))
            {
                return new Color(1f, 0f, 1f, alpha);
            }

            color.a = alpha;
            return color;
        }

        private static class UIFont
        {
            internal static readonly Font Value = Create();

            private static Font Create()
            {
                var systemFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Roboto", "SF Pro Display", "Helvetica Neue", "Arial" },
                    32);
                return systemFont ? systemFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private sealed class ButtonVisual
        {
            private readonly CanvasGroup _focus;
            private bool _hovered;

            internal ButtonVisual(RectTransform rect, CanvasGroup focus)
            {
                Rect = rect;
                _focus = focus;
            }

            internal RectTransform Rect { get; }

            internal void SetHovered(bool hovered)
            {
                _hovered = hovered;
            }

            internal void Tick(float deltaTime)
            {
                var interpolation = 1f - Mathf.Exp(-deltaTime * 18f);
                var targetScale = _hovered ? 1.035f : 1f;
                Rect.localScale = Vector3.Lerp(Rect.localScale, Vector3.one * targetScale, interpolation);
                _focus.alpha = Mathf.Lerp(_focus.alpha, _hovered ? 1f : 0f, interpolation);
            }
        }

        private enum PanelMode
        {
            Scanning,
            Result,
            Error
        }
    }

    internal static class ColorExtensions
    {
        internal static Color WithAlpha(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}
