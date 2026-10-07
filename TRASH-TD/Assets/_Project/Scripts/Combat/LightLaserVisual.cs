using System;
using UnityEngine;

namespace TrashTD.Combat
{
    public sealed class LightLaserVisual : MonoBehaviour
    {
        private static Material sharedMaterial;
        private Transform source;
        private Transform target;
        private float tickInterval;
        private float tickTimer;
        private Action onTick;

        public static LightLaserVisual Fire(Transform source, Transform target, Color color, float tickInterval, Action tick)
        {
            var laserObject = new GameObject("LightLaserVisual");
            var line = laserObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.075f;
            line.endWidth = 0.025f;
            line.startColor = color;
            line.endColor = new Color(color.r, color.g, color.b, 0.2f);
            line.sortingOrder = 41;
            line.sharedMaterial = GetMaterial();

            var visual = laserObject.AddComponent<LightLaserVisual>();
            visual.source = source;
            visual.target = target;
            visual.tickInterval = tickInterval;
            visual.onTick = tick;
            visual.line = line;
            visual.UpdateLine();
            return visual;
        }

        private LineRenderer line;

        private void Update()
        {
            if (source == null || target == null)
            {
                Stop();
                return;
            }

            UpdateLine();
            tickTimer += Time.deltaTime;
            while (tickTimer >= tickInterval)
            {
                tickTimer -= tickInterval;
                onTick?.Invoke();
            }
        }

        public void Stop()
        {
            onTick = null;
            Destroy(gameObject);
        }

        private void UpdateLine()
        {
            line.SetPosition(0, source.position);
            line.SetPosition(1, target.position);
        }

        private static Material GetMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader);
            sharedMaterial.name = "CastielLightLaserMaterial";
            return sharedMaterial;
        }
    }
}
