# PICO integration point

Add a `PicoQRScanner : MonoBehaviour, IQRScanner` here when PICO camera-frame access is selected.
The implementation should request the PICO camera permission, sample throttled RGB frames, decode them locally,
and emit the same `QRResult` event. Core flow, URL validation, and UI do not need to change.
