using QRLens.UI;
using UnityEngine;

namespace QRLens.Core
{
    public sealed class QRApplication : MonoBehaviour
    {
        private IQRScanner _scanner;
        private QRPanel _panel;
        private QRResult _currentResult;

        public void Initialize(IQRScanner scanner, QRPanel panel)
        {
            _scanner = scanner;
            _panel = panel;
            _scanner.QRDetected += OnQRDetected;
            _scanner.StateChanged += OnScannerStateChanged;
            _panel.OpenRequested += OpenCurrentResult;
            _panel.DismissRequested += DismissCurrentResult;
            _panel.ShowScanning("Starting scanner…");
            _scanner.StartScanning();
        }

        public void ProcessInput(Ray pointerRay, bool hasPointer, bool selectPressed, bool dismissPressed)
        {
            if (!_panel)
            {
                return;
            }

            if (hasPointer)
            {
                _panel.ProcessPointer(pointerRay, selectPressed);
            }
            else
            {
                _panel.ProcessPointer(selectPressed);
            }
            if (dismissPressed && _currentResult != null)
            {
                DismissCurrentResult();
            }
        }

        private void OnDestroy()
        {
            if (_scanner != null)
            {
                _scanner.QRDetected -= OnQRDetected;
                _scanner.StateChanged -= OnScannerStateChanged;
                _scanner.StopScanning();
            }

            if (_panel)
            {
                _panel.OpenRequested -= OpenCurrentResult;
                _panel.DismissRequested -= DismissCurrentResult;
            }
        }

        private void OnQRDetected(QRResult result)
        {
            if (_currentResult != null)
            {
                return;
            }

            _currentResult = result;
            _panel.ShowResult(result);
        }

        private void OnScannerStateChanged(QRScannerState state, string message)
        {
            if (_currentResult != null)
            {
                return;
            }

            switch (state)
            {
                case QRScannerState.Scanning:
                case QRScannerState.RequestingPermission:
                    _panel.ShowScanning(message);
                    break;
                case QRScannerState.PermissionDenied:
                case QRScannerState.Unavailable:
                case QRScannerState.Error:
                    _panel.ShowError(message);
                    break;
            }
        }

        private void OpenCurrentResult()
        {
            if (_currentResult == null || !URLLauncher.TryOpen(_currentResult.Payload))
            {
                return;
            }

            DismissCurrentResult();
        }

        private void DismissCurrentResult()
        {
            _currentResult = null;
            _panel.ShowScanning("Scanning…");
            _scanner.StartScanning();
        }
    }
}
