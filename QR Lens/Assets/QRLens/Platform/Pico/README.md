# PICO integration point

Add a `PicoQRScanner : MonoBehaviour, IQRScanner` here when PICO camera-frame access is selected.
The implementation should request the PICO camera permission, sample throttled frames, convert them to 8-bit
grayscale, call the shared `QRLens.Core.QRFrameDecoder`, and emit the same `QRResult` event. Core flow, URL
validation, and UI do not need to change.
