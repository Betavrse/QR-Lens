using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace QRLens.UI
{
    /// <summary>
    /// Lightweight rounded, gradient UI surface. It keeps QR Lens independent of
    /// bitmap nine-slices and renders cleanly at varying world-space canvas sizes.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedRectGraphic : MaskableGraphic
    {
        private const int SegmentsPerCorner = 7;

        [SerializeField] private float _cornerRadius = 24f;
        [SerializeField] private float _borderWidth;
        [SerializeField] private Color _topColor = Color.white;
        [SerializeField] private Color _bottomColor = Color.white;
        [SerializeField] private Color _borderColor = Color.clear;

        public void Configure(
            Color topColor,
            Color bottomColor,
            float cornerRadius,
            float borderWidth = 0f,
            Color? borderColor = null)
        {
            _topColor = topColor;
            _bottomColor = bottomColor;
            _cornerRadius = Mathf.Max(0f, cornerRadius);
            _borderWidth = Mathf.Max(0f, borderWidth);
            _borderColor = borderColor ?? Color.clear;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            var outerRect = GetPixelAdjustedRect();
            if (outerRect.width <= 0f || outerRect.height <= 0f)
            {
                return;
            }

            var outerRadius = Mathf.Min(_cornerRadius, Mathf.Min(outerRect.width, outerRect.height) * 0.5f);
            var outer = BuildPerimeter(outerRect, outerRadius);
            var borderWidth = Mathf.Min(_borderWidth, Mathf.Min(outerRect.width, outerRect.height) * 0.5f);

            if (borderWidth <= 0.01f)
            {
                AddFilledShape(vertexHelper, outer, outerRect);
                return;
            }

            var innerRect = new Rect(
                outerRect.xMin + borderWidth,
                outerRect.yMin + borderWidth,
                outerRect.width - borderWidth * 2f,
                outerRect.height - borderWidth * 2f);
            var innerRadius = Mathf.Max(0f, outerRadius - borderWidth);
            var inner = BuildPerimeter(innerRect, innerRadius);

            for (var index = 0; index < outer.Count; index++)
            {
                vertexHelper.AddVert(outer[index], _borderColor, Vector2.zero);
            }

            for (var index = 0; index < inner.Count; index++)
            {
                vertexHelper.AddVert(inner[index], GradientColor(inner[index].y, outerRect), Vector2.zero);
            }

            for (var index = 0; index < outer.Count; index++)
            {
                var next = (index + 1) % outer.Count;
                vertexHelper.AddTriangle(index, next, outer.Count + next);
                vertexHelper.AddTriangle(index, outer.Count + next, outer.Count + index);
            }

            var centerIndex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(outerRect.center, GradientColor(outerRect.center.y, outerRect), Vector2.zero);
            for (var index = 0; index < inner.Count; index++)
            {
                var next = (index + 1) % inner.Count;
                vertexHelper.AddTriangle(centerIndex, outer.Count + index, outer.Count + next);
            }
        }

        private void AddFilledShape(VertexHelper vertexHelper, IReadOnlyList<Vector2> perimeter, Rect rect)
        {
            vertexHelper.AddVert(rect.center, GradientColor(rect.center.y, rect), Vector2.zero);
            for (var index = 0; index < perimeter.Count; index++)
            {
                var point = perimeter[index];
                vertexHelper.AddVert(point, GradientColor(point.y, rect), Vector2.zero);
            }

            for (var index = 0; index < perimeter.Count; index++)
            {
                var next = (index + 1) % perimeter.Count;
                vertexHelper.AddTriangle(0, index + 1, next + 1);
            }
        }

        private Color32 GradientColor(float y, Rect rect)
        {
            var interpolation = Mathf.InverseLerp(rect.yMin, rect.yMax, y);
            return Color.Lerp(_bottomColor, _topColor, interpolation);
        }

        private static List<Vector2> BuildPerimeter(Rect rect, float radius)
        {
            var points = new List<Vector2>(SegmentsPerCorner * 4 + 4);
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, -90f, 0f);
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
            return points;
        }

        private static void AddCorner(
            ICollection<Vector2> points,
            Vector2 center,
            float radius,
            float startDegrees,
            float endDegrees)
        {
            for (var step = 0; step <= SegmentsPerCorner; step++)
            {
                var radians = Mathf.Lerp(startDegrees, endDegrees, step / (float)SegmentsPerCorner) * Mathf.Deg2Rad;
                points.Add(center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
            }
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            _cornerRadius = Mathf.Max(0f, _cornerRadius);
            _borderWidth = Mathf.Max(0f, _borderWidth);
            SetVerticesDirty();
        }
#endif
    }
}
