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

        private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();

        private Transform _head;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private EventSystem _eventSystem;
        private Text _eyebrow;
        private Text _payload;
        private Text _hint;
        private Button _openButton;
        private Button _dismissButton;
        private Button _hoveredButton;
        private bool _hasResult;

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
            _eyebrow.text = "QR LENS";
            _payload.text = string.IsNullOrWhiteSpace(message) ? "Scanning…" : message;
            _payload.color = Color.white;
            _hint.text = "Look at a QR code";
            _openButton.gameObject.SetActive(false);
            _dismissButton.gameObject.SetActive(false);
            PlaceInFrontOfUser();
        }

        public void ShowResult(QRResult result)
        {
            _hasResult = true;
            var payload = result.Payload ?? string.Empty;
            var hasUrl = result.HasTextPayload && URLLauncher.TryGetHttpUrl(payload, out _);

            _eyebrow.text = hasUrl ? "LINK DETECTED" : "QR DETECTED";
            _payload.text = FormatPayload(payload, result.HasTextPayload);
            _payload.color = Color.white;
            _hint.text = hasUrl
                ? "Aim at a button • Press trigger or A"
                : "This QR contains text, not a safe HTTP/HTTPS link";
            _openButton.gameObject.SetActive(hasUrl);
            _dismissButton.gameObject.SetActive(true);
            PlaceInFrontOfUser();
        }

        public void ShowError(string message)
        {
            _hasResult = false;
            _eyebrow.text = "SCANNER UNAVAILABLE";
            _payload.text = string.IsNullOrWhiteSpace(message) ? "QR scanning could not start." : message;
            _payload.color = new Color(1f, 0.72f, 0.65f);
            _hint.text = "Check permissions, then restart QR Lens";
            _openButton.gameObject.SetActive(false);
            _dismissButton.gameObject.SetActive(false);
            PlaceInFrontOfUser();
        }

        public void ProcessPointer(bool selectPressed)
        {
            if (!_hasResult || !_canvas || !_eventSystem)
            {
                SetHoveredButton(null, _eventSystem ? new PointerEventData(_eventSystem) : null);
                return;
            }

            ProcessScreenPointer(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), selectPressed);
        }

        public void ProcessPointer(Ray worldRay, bool selectPressed)
        {
            if (!_hasResult || !_canvas || !_eventSystem || !_canvas.worldCamera)
            {
                SetHoveredButton(null, _eventSystem ? new PointerEventData(_eventSystem) : null);
                return;
            }

            var panelPlane = new Plane(transform.forward, transform.position);
            if (!panelPlane.Raycast(worldRay, out var distance) || distance <= 0f)
            {
                SetHoveredButton(null, new PointerEventData(_eventSystem));
                return;
            }

            var screenPoint = _canvas.worldCamera.WorldToScreenPoint(worldRay.GetPoint(distance));
            if (screenPoint.z <= 0f)
            {
                SetHoveredButton(null, new PointerEventData(_eventSystem));
                return;
            }

            ProcessScreenPointer(screenPoint, selectPressed);
        }

        private void ProcessScreenPointer(Vector2 screenPosition, bool selectPressed)
        {
            var pointer = new PointerEventData(_eventSystem) { position = screenPosition };

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

            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(760f, 500f);
            rect.localScale = Vector3.one * 0.0012f;

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = head.GetComponent<Camera>();
            _canvas.sortingOrder = 10;
            _raycaster = gameObject.AddComponent<GraphicRaycaster>();

            var background = gameObject.AddComponent<Image>();
            background.color = new Color(0.035f, 0.045f, 0.055f, 0.96f);

            _eyebrow = CreateText("Status", transform, new Vector2(0f, 190f), new Vector2(650f, 40f), 24, FontStyle.Bold);
            _eyebrow.color = new Color(0.35f, 0.86f, 0.76f);
            _eyebrow.alignment = TextAnchor.MiddleCenter;

            _payload = CreateText("Payload", transform, new Vector2(0f, 65f), new Vector2(650f, 180f), 34, FontStyle.Normal);
            _payload.alignment = TextAnchor.MiddleCenter;

            _openButton = CreateButton("Open Link", new Vector2(-170f, -95f), new Color(0.14f, 0.66f, 0.56f));
            _openButton.onClick.AddListener(() => OpenRequested?.Invoke());

            _dismissButton = CreateButton("Dismiss / Scan Again", new Vector2(170f, -95f), new Color(0.20f, 0.23f, 0.27f));
            _dismissButton.onClick.AddListener(() => DismissRequested?.Invoke());

            _hint = CreateText("Hint", transform, new Vector2(0f, -195f), new Vector2(680f, 46f), 20, FontStyle.Normal);
            _hint.color = new Color(0.72f, 0.76f, 0.8f);
            _hint.alignment = TextAnchor.MiddleCenter;

            var reticle = CreateText("Reticle", transform, Vector2.zero, new Vector2(28f, 28f), 24, FontStyle.Bold);
            reticle.text = "•";
            reticle.color = new Color(1f, 1f, 1f, 0.55f);
            var reticleRect = (RectTransform)reticle.transform;
            reticleRect.anchorMin = new Vector2(0.5f, 0.5f);
            reticleRect.anchorMax = new Vector2(0.5f, 0.5f);
            reticleRect.anchoredPosition = Vector2.zero;

            ShowScanning("Scanning…");
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

            transform.position = _head.position + flatForward * 1.35f - Vector3.up * 0.08f;
            transform.rotation = Quaternion.LookRotation(transform.position - _head.position, Vector3.up);
        }

        private Button CreateButton(string label, Vector2 position, Color color)
        {
            var buttonObject = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(transform, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = new Vector2(290f, 78f);
            rect.anchoredPosition = position;

            var image = buttonObject.GetComponent<Image>();
            image.color = color;
            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var text = CreateText("Label", buttonObject.transform, Vector2.zero, rect.sizeDelta, 25, FontStyle.Bold);
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 position,
            Vector2 size,
            int fontSize,
            FontStyle style)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void SetHoveredButton(Button button, PointerEventData pointer)
        {
            if (_hoveredButton == button)
            {
                return;
            }

            if (_hoveredButton && pointer != null)
            {
                ExecuteEvents.Execute(_hoveredButton.gameObject, pointer, ExecuteEvents.pointerExitHandler);
            }

            _hoveredButton = button;
            if (_hoveredButton)
            {
                ExecuteEvents.Execute(_hoveredButton.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
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
    }
}
