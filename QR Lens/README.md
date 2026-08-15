# QR Lens

QR Lens is a free, minimal QR scanner for Meta Quest 3 and Quest 3S. It reads a forward-facing headset camera through Meta's Passthrough Camera API and decodes QR codes locally: no camera frames leave the device, and there are no accounts, backend, ads, analytics, history, or cloud services.

v0.1 flow:

```text
Passthrough → local camera-frame decoding → payload panel → Open Link / Dismiss
```

Only absolute `http://` and `https://` links with a valid host can be opened. Other QR payloads are displayed as text. URLs containing credentials, control characters, unsupported schemes, or more than 8,192 characters are rejected.

## Requirements

- Unity `6000.3.12f1` (Unity 6 LTS), installed through Unity Hub
- Android Build Support, Android SDK & NDK Tools, and OpenJDK modules for that editor
- A Meta Quest 3 or Quest 3S in Developer Mode, running a current Horizon OS release
- USB debugging enabled for sideloading

The package manifest pins the important XR dependencies: Meta XR Core SDK and MRUK `205.0.0`, Unity OpenXR `1.16.1`, Unity Meta OpenXR `2.4.0`, and XR Plug-in Management `4.5.3`. Package resolution can take several minutes on the first open.

Camera access uses MRUK's `PassthroughCameraAccess` component at a fixed 1280×960 resolution. QR decoding uses the Apache-2.0-licensed ZXing.Net `0.16.11` library included under `Assets/QRLens/Plugins/ZXing`. Frames are converted to grayscale, decoded on a worker thread, and immediately reused; they are not saved or transmitted. This path is used because the MRUK marker tracker can detect a native fiducial without delivering its payload callback on current Horizon OS builds.

## First-time project setup

1. Clone the repository and open its `QR Lens/` project folder in Unity `6000.3.12f1` (do not create another nested Unity project).
2. Let Unity finish resolving packages and compiling scripts.
3. Choose **Tools → QR Lens → Configure Quest Project** once.
4. Open **File → Build Profiles**, confirm Android is active, and use **Meta XR → Tools → Project Setup Tool** to review the project. The committed setup already configures the required items; apply any newly introduced mandatory fixes if a later Meta SDK reports them.
5. Open `Assets/QRLens/Scenes/Main.unity` if it is not already open.

The QR Lens setup command creates Unity's generated XR settings assets, assigns the Android OpenXR loader, and enables Meta XR, Meta lifecycle, Touch Controller, and Composition Layers OpenXR features. It also configures Quest 3/3S targets, passthrough, Headset Camera access, and a minimum Horizon OS SDK of 74. Scene and Anchor permissions are not requested. The prohibited experimental-features manifest flag remains disabled. These generated XR assets are intentionally produced by the installed package versions instead of being hand-maintained.

The Android manifest is committed at `Assets/Plugins/Android/AndroidManifest.xml`. The application ID is `com.qrlens.app`, minimum Android API level is 32, and ARM64/IL2CPP are enabled.

## Build and install

For a locally installable APK, choose:

**Tools → QR Lens → Build Quest APK**

The APK is written to:

```text
Builds/Android/QR-Lens-v1.0.apk
```

Install it with Meta Quest Developer Hub, or with Android Debug Bridge:

```sh
adb install -r Builds/Android/QR-Lens-v1.0.apk
```

In the headset, launch **QR Lens** from **Library → Unknown Sources**. Accept the requested **Headset Camera** permission; QR detection cannot work without it.

## Test on Quest

1. Display or print a normal QR code containing `https://example.com`.
2. Launch QR Lens and accept the Headset Camera permission prompt.
3. Look at the QR code while it is visible through passthrough.
4. Confirm the URL appears in the panel.
5. Aim a controller ray (or the center gaze reticle when no controller aim pose is available) at **Open Link**, then press either controller trigger or the A button.
6. Confirm the Quest browser opens `https://example.com`.
7. Repeat with a plain-text QR. Its text should appear, but **Open Link** should not.
8. Use **Dismiss / Scan Again**, or the B button, to resume scanning.

The scanner pauses result delivery as soon as it decodes a frame, preventing the same persistent QR from triggering repeatedly. **Dismiss / Scan Again** re-arms detection immediately without restarting the camera, and the camera stream resumes automatically after returning from the Quest browser.

The scanning UI stays as a compact card below the center of view so it does not cover the QR code. A successful scan expands into a larger result card with explicit link/text status, a protected payload surface, large controller targets, hover feedback, and an on-panel pointer cursor. Rounded surfaces and gradients are generated as resolution-independent UI geometry; the only bitmap used by the panel is the QR Lens logo.

If the panel remains on **Preparing camera…** or reports that scanning is unavailable, confirm that QR Lens has Headset Camera permission in Quest settings and inspect the device log:

```sh
adb logcat -s Unity
```

A healthy startup logs `passthrough camera active at 1280x960`. Every successful scan logs `decoded QR payload` without logging the payload itself.

## Architecture

```text
Assets/QRLens/
  Core/                 Shared result, scanner interface, QR decoder, app flow, URL validation
  Platform/Meta/        Quest camera provider and runtime bootstrap
  Platform/Pico/        Future integration notes
  UI/                   Platform-neutral world-space utility panel
  Editor/               Repeatable Quest setup and APK build commands
  Scenes/Main.unity     Minimal bootstrap scene
  Tests/EditMode/       Decoder, URL validation, and UI-state tests
```

`Core` and `UI` contain no Meta APIs. `MetaQRScanner` implements `IQRScanner`, captures Quest camera frames, calls the shared `QRFrameDecoder`, and emits a shared `QRResult`; `QRApplication` owns result state and UI behavior. A future PICO implementation can feed its camera frames through the same decoder and interface without changing the application or URL handling. See `Assets/QRLens/Platform/Pico/README.md`.

## Tests

Run the Edit Mode suite from **Window → General → Test Runner**. The tests cover QR frame decoding, safe and rejected URL payloads, and scanning/result/error UI behavior.

## v0.1 limitations

- Physical camera scanning must be tested on-device; ordinary Editor Play Mode does not expose Quest camera frames.
- Interaction uses head-gaze plus Quest controllers. Direct hand-ray/pinch interaction is deferred.
- There is no visual outline anchored to the detected QR yet.
- Camera access requires Quest 3/3S, Horizon OS v74 or newer, and Headset Camera permission.
- PICO is intentionally not implemented in v0.1.
- Release signing and Meta Store/App Lab packaging are outside this prototype build command.
- Unity's Development Build option is intentionally disabled: Meta XR Core 205 adds its editor-only Meta XR Operator API layer to Development APKs, and that layer can abort OpenXR startup on-device.

## Recommended v0.2

- Add first-class Meta Interaction SDK hand-ray/pinch input while preserving controller input.
- Add a subtle world-anchored marker at the detected QR pose.
- Add PICO's camera-frame permission/provider behind `IQRScanner` and reuse `QRFrameDecoder`.
- Add on-device smoke-test coverage and a signed release build profile.
