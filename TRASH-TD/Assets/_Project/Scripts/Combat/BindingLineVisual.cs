using System;
using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    public sealed class BindingLineVisual : MonoBehaviour
    {
        private static Material sharedMaterial;
        private Transform source;
        private OperatorBase target;
        private LineRenderer line;
        private float baseStartWidth = 0.08f;
        private float baseEndWidth = 0.05f;

        public static BindingLineVisual Create(Transform source, OperatorBase target, Color? color = null)
        {
            if (source == null || target == null)
                return null;

            Color lineColor = color ?? new Color(0.72f, 0.45f, 0.95f, 0.9f);
            var lineObj = new GameObject("BindingLineVisual");
            var lr = lineObj.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = 0.08f;
            lr.endWidth = 0.05f;
            lr.startColor = lineColor;
            lr.endColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.4f);
            lr.sortingOrder = 41;
            lr.sharedMaterial = GetMaterial();

            var visual = lineObj.AddComponent<BindingLineVisual>();
            visual.source = source;
            visual.target = target;
            visual.line = lr;
            visual.UpdateLine();
            return visual;
        }

        private void Update()
        {
            if (source == null || target == null || !target.IsDeployed || target.CurrentHP <= 0)
            {
                Stop();
                return;
            }

            float pulse = Mathf.PingPong(Time.time * 5f, 0.03f);
            line.startWidth = baseStartWidth + pulse;
            line.endWidth = baseEndWidth + pulse * 0.5f;

            UpdateLine();
        }

        public void Stop()
        {
            Destroy(gameObject);
        }

        private void UpdateLine()
        {
            if (line == null || source == null || target == null) return;
            line.SetPosition(0, source.position);
            line.SetPosition(1, target.transform.position);
        }

        private static Material GetMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader)
            {
                name = "BindingLineMaterial"
            };
            return sharedMaterial;
        }
    }
}

