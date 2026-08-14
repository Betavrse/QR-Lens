using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Meta.XR.MRUtilityKit;
using QRLens.Core;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace QRLens.Platform.Meta
{
    public sealed class MetaQRScanner : MonoBehaviour, IQRScanner
    {
        private const string ScenePermission = OVRPermissionsRequester.ScenePermission;
        private const float RescanDelaySeconds = 0.25f;
        private const float TrackablePollIntervalSeconds = 0.05f;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly List<MRUKTrackable> _trackables = new List<MRUKTrackable>();
        private Coroutine _startRoutine;
        private bool _subscribed;
        private bool _scanningRequested;
        private bool _applicationPaused;
        private float _earliestDetectionTime;
        private float _nextTrackablePollTime;

        public event Action<QRResult> QRDetected;

        public event Action<QRScannerState, string> StateChanged;

        public QRScannerState State { get; private set; } = QRScannerState.Stopped;

        public void StartScanning()
        {
            _scanningRequested = true;
            _earliestDetectionTime = Time.realtimeSinceStartup + RescanDelaySeconds;
            if (_applicationPaused)
            {
                return;
            }

            // StartScanning is intentionally idempotent. In particular, a UI action that
            // arrives during the browser-resume recovery must not cancel that recovery.
            if (_startRoutine != null)
            {
                return;
            }

            BeginStart(false);
        }

        private void Update()
        {
            if (!_scanningRequested || State != QRScannerState.Scanning || !MRUK.Instance ||
                Time.realtimeSinceStartup < _earliestDetectionTime ||
                Time.realtimeSinceStartup < _nextTrackablePollTime)
            {
                return;
            }

            _nextTrackablePollTime = Time.realtimeSinceStartup + TrackablePollIntervalSeconds;
            MRUK.Instance.GetTrackables(_trackables);
            foreach (var trackable in _trackables)
            {
                if (TryDetect(trackable))
                {
                    break;
                }
            }
        }

        public void StopScanning()
        {
            _scanningRequested = false;
            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
                _startRoutine = null;
            }

            SetTrackingEnabled(false);
            SetState(QRScannerState.Stopped, "Scanner stopped");
        }

        private void BeginStart(bool resetTracker)
        {
            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
            }

            _startRoutine = StartCoroutine(StartScanningRoutine(resetTracker));
        }

        private IEnumerator StartScanningRoutine(bool resetTracker)
        {
            SetState(QRScannerState.RequestingPermission, "Preparing scanner…");

            var timeoutAt = Time.realtimeSinceStartup + 10f;
            while (!MRUK.Instance && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (!MRUK.Instance)
            {
                SetState(QRScannerState.Error, "Meta MR services did not initialize.");
                _startRoutine = null;
                yield break;
            }

            Subscribe();

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(ScenePermission))
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
                Permission.RequestUserPermission(ScenePermission, callbacks);

                while (!permissionFinished)
                {
                    yield return null;
                }

                if (!permissionGranted && !Permission.HasUserAuthorizedPermission(ScenePermission))
                {
                    SetState(
                        QRScannerState.PermissionDenied,
                        "Room and spatial-data permission is required to detect QR codes.");
                    _startRoutine = null;
                    yield break;
                }
            }
#endif

            if (!MRUK.Instance.QRCodeTrackingSupported)
            {
                SetState(
                    QRScannerState.Unavailable,
                    "QR tracking is unavailable. Use a Quest 3/3S with current Horizon OS and grant spatial-data access.");
                _startRoutine = null;
                yield break;
            }

            if (resetTracker)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                var focusTimeoutAt = Time.realtimeSinceStartup + 5f;
                while (!OVRManager.hasVrFocus && Time.realtimeSinceStartup < focusTimeoutAt)
                {
                    yield return null;
                }
#endif

                // A browser transition can suspend MRUK while ConfigureTrackers is in flight.
                // Cycling the component invokes MRUK's supported OnDisable cleanup, which
                // clears the stale task and native tracker configuration before we re-enable it.
                SetTrackingEnabled(false);
                var mruk = MRUK.Instance;
                if (mruk && mruk.enabled)
                {
                    mruk.enabled = false;
                    yield return null;
                    mruk.enabled = true;
                    yield return null;
                }
            }

            if (!_scanningRequested || _applicationPaused)
            {
                _startRoutine = null;
                yield break;
            }

            SetTrackingEnabled(true);
            SetState(QRScannerState.Scanning, "Scanning…");
            _startRoutine = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _applicationPaused = true;
                if (_startRoutine != null)
                {
                    StopCoroutine(_startRoutine);
                    _startRoutine = null;
                }

                SetTrackingEnabled(false);
                if (_scanningRequested)
                {
                    SetState(QRScannerState.Paused, "Scanner suspended");
                }

                return;
            }

            var isReturningToApp = _applicationPaused;
            _applicationPaused = false;
            if (isReturningToApp && _scanningRequested)
            {
                BeginStart(true);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && MRUK.Instance)
            {
                MRUK.Instance.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            MRUK.Instance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _subscribed = true;
        }

        private void OnTrackableAdded(MRUKTrackable trackable)
        {
            TryDetect(trackable);
        }

        private bool TryDetect(MRUKTrackable trackable)
        {
            if (!_scanningRequested || State != QRScannerState.Scanning ||
                Time.realtimeSinceStartup < _earliestDetectionTime || !trackable || !trackable.IsTracked ||
                trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return false;
            }

            var payload = ReadPayload(trackable, out var hasTextPayload);

            // Pause immediately so a persistent marker cannot trigger the app every frame.
            _scanningRequested = false;
            SetTrackingEnabled(false);
            SetState(QRScannerState.Paused, "QR detected");
            QRDetected?.Invoke(
                new QRResult(payload, trackable.transform.position, trackable.transform.rotation, hasTextPayload));
            return true;
        }

        private static string ReadPayload(MRUKTrackable trackable, out bool hasTextPayload)
        {
            if (trackable.MarkerPayloadString != null)
            {
                hasTextPayload = true;
                return trackable.MarkerPayloadString;
            }

            if (trackable.MarkerPayloadBytes is { Length: > 0 } bytes)
            {
                try
                {
                    hasTextPayload = true;
                    return StrictUtf8.GetString(bytes);
                }
                catch (DecoderFallbackException)
                {
                    hasTextPayload = false;
                    var preview = string.Join(" ", bytes.Take(24).Select(value => value.ToString("X2")));
                    return $"Binary QR payload ({bytes.Length} bytes): {preview}{(bytes.Length > 24 ? " …" : string.Empty)}";
                }
            }

            hasTextPayload = true;
            return string.Empty;
        }

        private static void SetTrackingEnabled(bool enabled)
        {
            if (!MRUK.Instance)
            {
                return;
            }

            var configuration = MRUK.Instance.SceneSettings.TrackerConfiguration;
            configuration.QRCodeTrackingEnabled = enabled;
            MRUK.Instance.SceneSettings.TrackerConfiguration = configuration;
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
    }
}
