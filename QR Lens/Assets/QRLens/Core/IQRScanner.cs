using System;

namespace QRLens.Core
{
    public enum QRScannerState
    {
        Stopped,
        RequestingPermission,
        Scanning,
        Paused,
        Unavailable,
        PermissionDenied,
        Error
    }

    public interface IQRScanner
    {
        event Action<QRResult> QRDetected;

        event Action<QRScannerState, string> StateChanged;

        QRScannerState State { get; }

        void StartScanning();

        void StopScanning();
    }
}
