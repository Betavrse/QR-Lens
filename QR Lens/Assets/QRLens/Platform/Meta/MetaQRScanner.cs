using System;
using System.Collections;
using System.Threading.Tasks;
using Meta.XR;
using QRLens.Core;
using Unity.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace QRLens.Platform.Meta
{
    /// <summary>
    /// Quest QR scanner backed by Meta's Passthrough Camera API and an on-device decoder.
    /// Camera pixels are sampled locally and are never stored or transmitted.
    /// </summary>
    public sealed class MetaQRScanner : MonoBehaviour, IQRScanner
    {
        private const string CameraPermission = OVRPermissionsRequester.PassthroughCameraAccessPermission;
        private const float CaptureIntervalSeconds = 0.15f;
        private const float RescanDelaySeconds = 0.2f;
        private static readonly Vector2Int RequestedResolution = new Vector2Int(1280, 960);

        private Coroutine _startRoutine;
        private PassthroughCameraAccess _cameraAccess;
        private Task<DecodeOutcome> _decodeTask;
        private byte[] _luminanceBuffer;
        private bool _scanningRequested;
        private bool _acceptDetections;
        private bool _applicationPaused;
        private bool _cameraPermissionRequestInProgress;
        private float _earliestDetectionTime;
        private float _nextCaptureTime;
        private int _scanGeneration;

        public event Action<QRResult> QRDetected;

        public event Action<QRScannerState, string> StateChanged;

        public QRScannerState State { get; private set; } = QRScannerState.Stopped;

        public void StartScanning()
        {
            _scanningRequested = true;
            _acceptDetections = true;
            _scanGeneration++;
            _earliestDetectionTime = Time.realtimeSinceStartup + RescanDelaySeconds;

            if (_applicationPaused || _startRoutine != null)
            {
                return;
            }

            if (_cameraAccess && _cameraAccess.IsPlaying)
            {
                SetState(QRScannerState.Scanning, "Scanning…");
                return;
            }

            _startRoutine = StartCoroutine(StartCameraRoutine());
        }

        public void StopScanning()
        {
            _scanningRequested = false;
            _acceptDetections = false;
            _scanGeneration++;

            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
                _startRoutine = null;
            }

            if (_cameraAccess)
            {
                _cameraAccess.enabled = false;
            }

            SetState(QRScannerState.Stopped, "Scanner stopped");
        }

        private void Update()
        {
            CompleteDecodeIfReady();

            if (!_scanningRequested || !_acceptDetections || _applicationPaused ||
                !_cameraAccess || !_cameraAccess.IsPlaying)
            {
                return;
            }

            if (State != QRScannerState.Scanning)
            {
                SetState(QRScannerState.Scanning, "Scanning…");
            }

            if (_decodeTask != null || Time.realtimeSinceStartup < _earliestDetectionTime ||
                Time.realtimeSinceStartup < _nextCaptureTime || !_cameraAccess.IsUpdatedThisFrame)
            {
                return;
            }

            _nextCaptureTime = Time.realtimeSinceStartup + CaptureIntervalSeconds;
            CaptureAndDecode(_scanGeneration);
        }

        private IEnumerator StartCameraRoutine()
        {
            SetState(QRScannerState.RequestingPermission, "Preparing camera…");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(CameraPermission))
            {
                var permissionFinished = false;
                var permissionGranted = false;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ =>
                {
                    permissionGranted = true;
                    permissionFinished = true;
                };
                callbacks.PermissionDenied += _ => permissionFinished = true;

                _cameraPermissionRequestInProgress = true;
                Permission.RequestUserPermission(CameraPermission, callbacks);
                while (!permissionFinished)
                {
                    yield return null;
                }

                _cameraPermissionRequestInProgress = false;
                if (!permissionGranted && !Permission.HasUserAuthorizedPermission(CameraPermission))
                {
                    SetState(
                        QRScannerState.PermissionDenied,
                        "Headset camera permission is required to scan QR codes.");
                    _startRoutine = null;
                    yield break;
                }
            }
#endif

            if (!PassthroughCameraAccess.IsSupported)
            {
                SetState(
                    QRScannerState.Unavailable,
                    "Camera scanning requires Quest 3 or Quest 3S with Horizon OS v74 or newer.");
                _startRoutine = null;
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            while (_scanningRequested && !_applicationPaused && !OVRManager.hasVrFocus)
            {
                yield return null;
            }
#endif

            if (!_scanningRequested || _applicationPaused)
            {
                _startRoutine = null;
                yield break;
            }

            EnsureCameraAccess();
            if (!_cameraAccess.enabled)
            {
                _cameraAccess.enabled = true;
            }

            var timeoutAt = Time.realtimeSinceStartup + 12f;
            while (_scanningRequested && !_applicationPaused && !_cameraAccess.IsPlaying &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (!_scanningRequested || _applicationPaused)
            {
                _startRoutine = null;
                yield break;
            }

            if (!_cameraAccess.IsPlaying)
            {
                SetState(
                    QRScannerState.Error,
                    "Quest could not start the headset camera. Check Camera permission, then restart QR Lens.");
                _startRoutine = null;
                yield break;
            }

            Debug.Log(
                $"QR Lens scanner: passthrough camera active at " +
                $"{_cameraAccess.CurrentResolution.x}x{_cameraAccess.CurrentResolution.y}.");
            SetState(
                _acceptDetections ? QRScannerState.Scanning : QRScannerState.Paused,
                _acceptDetections ? "Scanning…" : "QR detected");
            _startRoutine = null;
        }

        private void EnsureCameraAccess()
        {
            if (_cameraAccess)
            {
                return;
            }

            var cameraObject = new GameObject("QR Lens Passthrough Camera");
            cameraObject.SetActive(false);
            _cameraAccess = cameraObject.AddComponent<PassthroughCameraAccess>();
            _cameraAccess.enabled = false;
            _cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            _cameraAccess.RequestedResolution = RequestedResolution;
            _cameraAccess.MaxFramerate = 30;
            cameraObject.SetActive(true);
            _cameraAccess.enabled = true;
        }

        private void CaptureAndDecode(int generation)
        {
            NativeArray<Color32> colors;
            try
            {
                colors = _cameraAccess.GetColors();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"QR Lens scanner: camera readback failed: {exception.Message}");
                return;
            }

            var resolution = _cameraAccess.CurrentResolution;
            var pixelCount = resolution.x * resolution.y;
            if (!colors.IsCreated || resolution.x <= 0 || resolution.y <= 0 || colors.Length < pixelCount)
            {
                return;
            }

            if (_luminanceBuffer == null || _luminanceBuffer.Length != pixelCount)
            {
                _luminanceBuffer = new byte[pixelCount];
            }

            for (var index = 0; index < pixelCount; index++)
            {
                var color = colors[index];
                _luminanceBuffer[index] = (byte)((color.r * 77 + color.g * 150 + color.b * 29) >> 8);
            }

            var luminance = _luminanceBuffer;
            var width = resolution.x;
            var height = resolution.y;
            _decodeTask = Task.Run(() => Decode(luminance, width, height, generation));
        }

        private void CompleteDecodeIfReady()
        {
            if (_decodeTask == null || !_decodeTask.IsCompleted)
            {
                return;
            }

            var completedTask = _decodeTask;
            _decodeTask = null;

            if (completedTask.IsFaulted)
            {
                var message = completedTask.Exception?.GetBaseException().Message ?? "Unknown decoder error";
                Debug.LogWarning($"QR Lens scanner: decoder failed: {message}");
                return;
            }

            if (completedTask.IsCanceled || completedTask.Result == null ||
                completedTask.Result.Generation != _scanGeneration || !_scanningRequested ||
                !_acceptDetections || State != QRScannerState.Scanning ||
                Time.realtimeSinceStartup < _earliestDetectionTime)
            {
                return;
            }

            var outcome = completedTask.Result;
            _acceptDetections = false;
            SetState(QRScannerState.Paused, "QR detected");
            Debug.Log($"QR Lens scanner: decoded QR payload ({outcome.Payload.Length} characters).");

            var head = Camera.main ? Camera.main.transform : null;
            var position = head ? head.position + head.forward * 1.5f : Vector3.zero;
            var rotation = head ? head.rotation : Quaternion.identity;
            QRDetected?.Invoke(new QRResult(outcome.Payload, position, rotation, outcome.HasTextPayload));
        }

        private static DecodeOutcome Decode(byte[] luminance, int width, int height, int generation)
        {
            if (!QRFrameDecoder.TryDecodeLuminance(
                    luminance,
                    width,
                    height,
                    out var payload,
                    out var hasTextPayload))
            {
                return null;
            }

            return new DecodeOutcome(generation, payload, hasTextPayload);
        }

        private void OnApplicationPause(bool paused)
        {
            if (_cameraPermissionRequestInProgress)
            {
                return;
            }

            if (paused)
            {
                _applicationPaused = true;
                if (_startRoutine != null)
                {
                    StopCoroutine(_startRoutine);
                    _startRoutine = null;
                }

                if (_scanningRequested)
                {
                    SetState(QRScannerState.Paused, "Scanner suspended");
                }

                return;
            }

            var returningToApp = _applicationPaused;
            _applicationPaused = false;
            if (returningToApp && _scanningRequested && _startRoutine == null)
            {
                _startRoutine = StartCoroutine(StartCameraRoutine());
            }
        }

        private void OnDestroy()
        {
            _scanGeneration++;
            if (_cameraAccess)
            {
                Destroy(_cameraAccess.gameObject);
            }
        }

        private void SetState(QRScannerState state, string message)
        {
            State = state;
            if (state == QRScannerState.Error || state == QRScannerState.PermissionDenied ||
                state == QRScannerState.Unavailable)
            {
                Debug.LogWarning($"QR Lens scanner: {state} — {message}");
            }
            else
            {
                Debug.Log($"QR Lens scanner: {state} — {message}");
            }

            StateChanged?.Invoke(state, message);
        }

        private sealed class DecodeOutcome
        {
            public DecodeOutcome(int generation, string payload, bool hasTextPayload)
            {
                Generation = generation;
                Payload = payload ?? string.Empty;
                HasTextPayload = hasTextPayload;
            }

            public int Generation { get; }

            public string Payload { get; }

            public bool HasTextPayload { get; }
        }
    }
}
