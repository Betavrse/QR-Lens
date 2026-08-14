# QR Lens

QR Lens is a free, minimal QR scanner for Meta Quest 3 and Quest 3S. It uses the headset's native Meta MR Utility Kit (MRUK) QR tracking: no camera frames leave the device, and there are no accounts, backend, ads, analytics, history, or cloud services.

v0.1 flow:

```text
Passthrough → native QR detection → payload panel → Open Link / Dismiss
```

Only absolute `http://` and `https://` links with a valid host can be opened. Other QR payloads are displayed as text. URLs containing credentials, control characters, unsupported schemes, or more than 8,192 characters are rejected.

## Requirements

- Unity `6000.3.12f1` (Unity 6 LTS), installed through Unity Hub
- Android Build Support, Android SDK & NDK Tools, and OpenJDK modules for that editor
- A Meta Quest 3 or Quest 3S in Developer Mode, running a current Horizon OS release
- USB debugging enabled for sideloading

The package manifest pins the important XR dependencies: Meta XR Core SDK and MRUK `205.0.0`, Unity OpenXR `1.16.1`, Unity Meta OpenXR `2.4.0`, and XR Plug-in Management `4.5.3`. Package resolution can take several minutes on the first open.

Meta's native QR tracking is used instead of a third-party decoder or raw-camera pipeline. The implementation follows the current [Meta MRUK sample](https://github.com/oculus-samples/Unity-MRUtilityKitSample) and reads `MRUKTrackable.MarkerPayloadString` / `MarkerPayloadBytes`.

## First-time project setup

1. Clone the repository and open its root in Unity `6000.3.12f1`.
2. Let Unity finish resolving packages and compiling scripts.
3. Choose **Tools → QR Lens → Configure Quest Project** once.
4. Open **File → Build Profiles**, confirm Android is active, and use **Meta XR → Tools → Project Setup Tool** to review the project. The committed setup already configures the required items; apply any newly introduced mandatory fixes if a later Meta SDK reports them.
5. Open `Assets/QRLens/Scenes/Main.unity` if it is not already open.

The QR Lens setup command creates Unity's generated XR settings assets, assigns the Android OpenXR loader, and enables Meta XR, Meta lifecycle, Touch Controller, and Composition Layers OpenXR features. It also configures Quest 3/3S targets, passthrough, Scene API access, and experimental features required by Meta QR tracking. These generated XR assets are intentionally produced by the installed package versions instead of being hand-maintained.

The Android manifest is committed at `Assets/Plugins/Android/AndroidManifest.xml`. The application ID is `com.qrlens.app`, minimum Android API level is 32, and ARM64/IL2CPP are enabled.

## Build and install

For a locally installable APK, choose:

**Tools → QR Lens → Build Quest APK**

The APK is written to:

```text
Builds/Android/QR-Lens-v0.1.apk
```

Install it with Meta Quest Developer Hub, or with Android Debug Bridge:

```sh
adb install -r Builds/Android/QR-Lens-v0.1.apk
```

In the headset, launch **QR Lens** from **Library → Unknown Sources**. Accept the requested spatial-data/room permission; QR detection cannot work without it.

## Test on Quest

1. Display or print a normal QR code containing `https://example.com`.
2. Launch QR Lens and accept the permission prompt.
3. Look at the QR code while it is visible through passthrough.
4. Confirm the URL appears in the panel.
5. Aim the center reticle at **Open Link**, then press either controller trigger or the A button.
6. Confirm the Quest browser opens `https://example.com`.
7. Repeat with a plain-text QR. Its text should appear, but **Open Link** should not.
8. Use **Dismiss / Scan Again**, or the B button, to resume scanning.

The scanner pauses as soon as it reports a marker, preventing the same persistent QR from triggering every frame. Scanning resumes only after dismissal.

## Architecture

```text
Assets/QRLens/
  Core/                 Platform-neutral result, scanner interface, app flow, URL validation
  Platform/Meta/        MRUK scanner and Quest runtime bootstrap
  Platform/Pico/        Future integration notes
  UI/                   Platform-neutral world-space utility panel
  Editor/               Repeatable Quest setup and APK build commands
  Scenes/Main.unity     Minimal bootstrap scene
  Tests/EditMode/       URL validation tests
```

`Core` and `UI` contain no Meta APIs. `MetaQRScanner` implements `IQRScanner`, then emits a shared `QRResult`; `QRApplication` owns result state and UI behavior. A future PICO implementation can feed decoded camera-frame results through the same interface without changing the application or URL handling. See `Assets/QRLens/Platform/Pico/README.md`.

## Tests

Run the Edit Mode suite from **Window → General → Test Runner**. The current tests cover accepted HTTP/HTTPS links and rejected malformed, credential-bearing, non-web, hostless, control-character, and oversized payloads.

## v0.1 limitations

- Physical QR tracking must be tested on-device; it is not available in ordinary Editor Play Mode or Quest Link.
- Interaction uses head-gaze plus Quest controllers. Direct hand-ray/pinch interaction is deferred.
- There is no visual outline anchored to the detected QR yet.
- QR tracking availability depends on Meta's native runtime support and the spatial-data permission.
- PICO is intentionally not implemented in v0.1.
- Release signing and Meta Store/App Lab packaging are outside this prototype build command.
- Unity's Development Build option is intentionally disabled: Meta XR Core 205 adds its editor-only Meta XR Operator API layer to Development APKs, and that layer can abort OpenXR startup on-device.

## Recommended v0.2

- Add first-class Meta Interaction SDK hand-ray/pinch input while preserving controller input.
- Add a subtle world-anchored marker at the detected QR pose.
- Add PICO's camera-frame permission/provider behind `IQRScanner` and a local QR decoder only for that platform.
- Add on-device smoke-test coverage and a signed release build profile.
