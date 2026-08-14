using UnityEngine;

namespace QRLens.Core
{
    public sealed class QRResult
    {
        public QRResult(string payload, Vector3 position, Quaternion rotation, bool hasTextPayload = true)
        {
            Payload = payload ?? string.Empty;
            Position = position;
            Rotation = rotation;
            HasTextPayload = hasTextPayload;
        }

        public string Payload { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public bool HasTextPayload { get; }
    }
}
