using System.Collections.Generic;
using NUnit.Framework;
using QRLens.Core;
using QRLens.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QRLens.Tests
{
    public sealed class QRPanelTests
    {
        private GameObject _cameraObject;
        private QRPanel _panel;
        private EventSystem _createdEventSystem;

        [SetUp]
        public void SetUp()
        {
            var existingEventSystem = Object.FindFirstObjectByType<EventSystem>();
            _cameraObject = new GameObject("QR Panel Test Camera");
            _cameraObject.AddComponent<Camera>();
            _panel = QRPanel.Create(_cameraObject.transform);
            _createdEventSystem = existingEventSystem
                ? null
                : Object.FindFirstObjectByType<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_panel)
            {
                Object.DestroyImmediate(_panel.gameObject);
            }

            if (_cameraObject)
            {
                Object.DestroyImmediate(_cameraObject);
            }

            if (_createdEventSystem)
            {
                Object.DestroyImmediate(_createdEventSystem.gameObject);
            }
        }

        [Test]
        public void ShowScanning_UsesCompactViewAndHidesActions()
        {
            _panel.ShowScanning("Scanning…");

            Assert.That(Find<RectTransform>("Scanning View").gameObject.activeInHierarchy, Is.True);
            Assert.That(Find<RectTransform>("Result View").gameObject.activeInHierarchy, Is.False);
            Assert.That(Find<Button>("Open Link").gameObject.activeInHierarchy, Is.False);
            Assert.That(Find<Button>("Dismiss / Scan Again").gameObject.activeInHierarchy, Is.False);
            Assert.That(Find<Text>("Scanning Title").text, Is.EqualTo("Looking for a QR code"));
        }

        [Test]
        public void ShowResult_SafeUrlShowsBothActions()
        {
            _panel.ShowResult(Result("https://example.com"));

            Assert.That(Find<Text>("Status Label").text, Is.EqualTo("LINK READY"));
            Assert.That(Find<Text>("Content Type").text, Is.EqualTo("WEB LINK"));
            Assert.That(Find<Button>("Open Link").gameObject.activeInHierarchy, Is.True);
            Assert.That(Find<Button>("Dismiss / Scan Again").gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public void ShowResult_TextPayloadHidesOpenAndDoesNotInterpretRichText()
        {
            const string payload = "<b>Not markup</b>";
            _panel.ShowResult(Result(payload));

            var payloadText = Find<Text>("Payload");
            Assert.That(Find<Text>("Status Label").text, Is.EqualTo("TEXT FOUND"));
            Assert.That(Find<Button>("Open Link").gameObject.activeInHierarchy, Is.False);
            Assert.That(Find<Button>("Dismiss / Scan Again").gameObject.activeInHierarchy, Is.True);
            Assert.That(payloadText.text, Is.EqualTo(payload));
            Assert.That(payloadText.supportRichText, Is.False);
        }

        [Test]
        public void ShowError_UsesDedicatedErrorState()
        {
            const string message = "Headset camera permission is required.";
            _panel.ShowError(message);

            Assert.That(Find<RectTransform>("Error View").gameObject.activeInHierarchy, Is.True);
            Assert.That(Find<RectTransform>("Scanning View").gameObject.activeInHierarchy, Is.False);
            Assert.That(Find<Text>("Error Message").text, Is.EqualTo(message));
        }

        [Test]
        public void ProcessPointer_ControllerRayInvokesOpenAction()
        {
            var openRequested = false;
            _panel.OpenRequested += () => openRequested = true;
            _panel.ShowResult(Result("https://example.com"));
            Find<RectTransform>("Card").sizeDelta = new Vector2(840f, 560f);
            Canvas.ForceUpdateCanvases();

            var target = Find<Button>("Open Link").transform.position;
            var camera = _cameraObject.GetComponent<Camera>();
            camera.Render();
            Canvas.ForceUpdateCanvases();
            var screenPoint = camera.WorldToScreenPoint(target);
            Assert.That(screenPoint.z, Is.GreaterThan(0f));
            Assert.That(
                RectTransformUtility.RectangleContainsScreenPoint(
                    Find<RectTransform>("Card"),
                    screenPoint,
                    camera),
                Is.True);

            var pointer = new PointerEventData(_createdEventSystem) { position = screenPoint };
            var results = new List<RaycastResult>();
            _panel.GetComponent<GraphicRaycaster>().Raycast(pointer, results);
            Assert.That(
                results.Exists(result => result.gameObject.name == "Open Link"),
                Is.True,
                $"Raycast results: {string.Join(", ", results.ConvertAll(result => result.gameObject.name))}");

            var ray = new Ray(
                _cameraObject.transform.position,
                (target - _cameraObject.transform.position).normalized);
            _panel.ProcessPointer(ray, true);

            Assert.That(openRequested, Is.True);
        }

        private T Find<T>(string objectName) where T : Component
        {
            foreach (var component in _panel.GetComponentsInChildren<T>(true))
            {
                if (component.gameObject.name == objectName)
                {
                    return component;
                }
            }

            Assert.Fail($"Could not find {typeof(T).Name} named {objectName} in the QR panel.");
            return null;
        }

        private static QRResult Result(string payload)
        {
            return new QRResult(payload, Vector3.zero, Quaternion.identity);
        }
    }
}
