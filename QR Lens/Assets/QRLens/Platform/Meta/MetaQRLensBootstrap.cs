using System.Collections;
using Meta.XR.MRUtilityKit;
using QRLens.Core;
using QRLens.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering.Universal;

namespace QRLens.Platform.Meta
{
    public sealed class MetaQRLensBootstrap : MonoBehaviour
    {
        private QRApplication _application;
        private LineRenderer _leftPointer;
        private LineRenderer _rightPointer;
        private Transform _trackingSpace;

        private IEnumerator Start()
        {
            var cameraRig = CreateCameraRig();
            CreatePassthrough();
            CreateMRUK();

            var timeoutAt = Time.realtimeSinceStartup + 10f;
            while ((!cameraRig.centerEyeAnchor || !cameraRig.centerEyeAnchor.GetComponent<Camera>()) &&
                   Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (!cameraRig.centerEyeAnchor)
            {
                Debug.LogError("QR Lens could not initialize the Quest camera rig.");
                yield break;
            }

            var camera = cameraRig.centerEyeAnchor.GetComponent<Camera>();
            if (camera)
            {
                if (!camera.GetComponent<UniversalAdditionalCameraData>())
                {
                    camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }

                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
            }

            var scanner = new GameObject("Meta QR Scanner").AddComponent<MetaQRScanner>();
            var panel = QRPanel.Create(cameraRig.centerEyeAnchor);
            _trackingSpace = cameraRig.trackingSpace;
            _leftPointer = CreateControllerPointer("Left Controller Pointer");
            _rightPointer = CreateControllerPointer("Right Controller Pointer");
            _application = gameObject.AddComponent<QRApplication>();
            _application.Initialize(scanner, panel);
        }

        private void Update()
        {
            if (!_application)
            {
                return;
            }

            var rightTracked = UpdateControllerAimPose(_rightPointer, CommonUsages.RightHand);
            var leftTracked = UpdateControllerAimPose(_leftPointer, CommonUsages.LeftHand);
            var pointerTransform = rightTracked
                ? _rightPointer.transform
                : leftTracked
                    ? _leftPointer.transform
                    : null;

            _rightPointer.enabled = rightTracked;
            _leftPointer.enabled = !rightTracked && leftTracked;

            var selectPressed = OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger) ||
                                OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger) ||
                                OVRInput.GetDown(OVRInput.RawButton.A) ||
                                OVRInput.GetDown(OVRInput.RawButton.X);
            var dismissPressed = OVRInput.GetDown(OVRInput.RawButton.B) ||
                                 OVRInput.GetDown(OVRInput.RawButton.Y);
            var pointerRay = pointerTransform
                ? new Ray(pointerTransform.position, pointerTransform.forward)
                : default;
            _application.ProcessInput(pointerRay, pointerTransform != null, selectPressed, dismissPressed);
        }

        private static OVRCameraRig CreateCameraRig()
        {
            var existingRig = FindFirstObjectByType<OVRCameraRig>();
            if (existingRig)
            {
                return existingRig;
            }

            var rigObject = new GameObject("QR Lens Camera Rig");
            var manager = rigObject.AddComponent<OVRManager>();
            manager.isInsightPassthroughEnabled = true;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            return rigObject.AddComponent<OVRCameraRig>();
        }

        private static void CreatePassthrough()
        {
            if (FindFirstObjectByType<OVRPassthroughLayer>())
            {
                return;
            }

            new GameObject("Quest Passthrough").AddComponent<OVRPassthroughLayer>();
        }

        private bool UpdateControllerAimPose(LineRenderer pointer, InternedString handUsage)
        {
            if (!pointer || !_trackingSpace)
            {
                return false;
            }

            foreach (var device in InputSystem.devices)
            {
                if (!device.added || !HasUsage(device, handUsage))
                {
                    continue;
                }

                var isTracked = device.TryGetChildControl<ButtonControl>("isTracked");
                var pointerPosition = device.TryGetChildControl<Vector3Control>("pointerPosition");
                var pointerRotation = device.TryGetChildControl<QuaternionControl>("pointerRotation");
                if (isTracked == null || !isTracked.isPressed ||
                    pointerPosition == null || pointerRotation == null)
                {
                    continue;
                }

                var localPosition = pointerPosition.ReadValue();
                var localRotation = pointerRotation.ReadValue();
                pointer.transform.SetPositionAndRotation(
                    _trackingSpace.TransformPoint(localPosition),
                    _trackingSpace.rotation * localRotation);
                return true;
            }

            return false;
        }

        private static bool HasUsage(InputDevice device, InternedString usage)
        {
            foreach (var deviceUsage in device.usages)
            {
                if (deviceUsage == usage)
                {
                    return true;
                }
            }

            return false;
        }

        private static LineRenderer CreateControllerPointer(string name)
        {
            var pointerObject = new GameObject(name);

            var line = pointerObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * 4f);
            line.startWidth = 0.006f;
            line.endWidth = 0.002f;
            line.startColor = new Color(0.35f, 0.86f, 0.76f, 0.9f);
            line.endColor = new Color(0.35f, 0.86f, 0.76f, 0.25f);
            line.numCapVertices = 4;
            line.sortingOrder = 20;

            var shader = Shader.Find("QRLens/ControllerPointer");
            if (!shader)
            {
                Debug.LogError("QR Lens controller pointer shader is missing; pointer rendering is disabled.");
                line.enabled = false;
                return line;
            }

            line.material = new Material(shader);

            line.enabled = false;
            return line;
        }

        private static void CreateMRUK()
        {
            var mruk = MRUK.Instance ? MRUK.Instance : new GameObject("MRUK").AddComponent<MRUK>();
            // Components created at runtime do not receive the serialized defaults that a
            // scene-authored MRUK component gets. MRUK.Update dereferences SceneSettings.
            mruk.SceneSettings ??= new MRUK.MRUKSettings();
            mruk.SceneSettings.LoadSceneOnStartup = false;
            var configuration = mruk.SceneSettings.TrackerConfiguration;
            configuration.QRCodeTrackingEnabled = true;
            mruk.SceneSettings.TrackerConfiguration = configuration;
        }
    }
}
