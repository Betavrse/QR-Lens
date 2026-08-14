using System;
using System.Collections;
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
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private Coroutine _startRoutine;
        private bool _subscribed;

        public event Action<QRResult> QRDetected;

        public event Action<QRScannerState, string> StateChanged;

        public QRScannerState State { get; private set; } = QRScannerState.Stopped;

        public void StartScanning()
        {
            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
            }

            _startRoutine = StartCoroutine(StartScanningRoutine());
        }

        public void StopScanning()
        {
            if (_startRoutine != null)
            {
                StopCoroutine(_startRoutine);
                _startRoutine = null;
            }

            SetTrackingEnabled(false);
            SetState(QRScannerState.Stopped, "Scanner stopped");
        }

        private IEnumerator StartScanningRoutine()
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
                    yield break;
                }
            }
#endif

            if (!MRUK.Instance.QRCodeTrackingSupported)
            {
                SetState(
                    QRScannerState.Unavailable,
                    "QR tracking is unavailable. Use a Quest 3/3S with current Horizon OS and grant spatial-data access.");
                yield break;
            }

            SetTrackingEnabled(true);
            SetState(QRScannerState.Scanning, "Scanning…");
            _startRoutine = null;
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
            if (State != QRScannerState.Scanning || trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return;
            }

            var payload = ReadPayload(trackable, out var hasTextPayload);

            // Pause immediately so a persistent marker cannot trigger the app every frame.
            SetTrackingEnabled(false);
            SetState(QRScannerState.Paused, "QR detected");
            QRDetected?.Invoke(
                new QRResult(payload, trackable.transform.position, trackable.transform.rotation, hasTextPayload));
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
