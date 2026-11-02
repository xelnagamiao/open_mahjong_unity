using UnityEngine;
using UnityEngine.InputSystem;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PointerClickEffect : UnityEngine.UI.Graphic
    {
        const float Lifetime = 0.5f;
        const float Intensity = 0.8f;
        public const float SizeScale = 1.25f;

        struct Pulse
        {
            public Vector2 point;
            public float started;
            public uint seed;
            public bool active;
        }

        readonly Pulse[] pulses = new Pulse[32];
        int nextPulse;
        uint randomState;
        bool hasPulses;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            var mouse = Mouse.current;
            if (Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
                Emit(mouse.position.ReadValue());
            if (hasPulses) SetVerticesDirty();
        }

        void Emit(Vector2 screenPoint)
        {
            if (screenPoint.x < 0 || screenPoint.y < 0 || screenPoint.x >= Screen.width || screenPoint.y >= Screen.height)
                return;
            var owner = canvas;
            if (owner == null || !owner.isActiveAndEnabled) return;
            var camera = owner.renderMode == RenderMode.ScreenSpaceOverlay ? null : owner.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, camera, out var localPoint))
                return;
            if (!rectTransform.rect.Contains(localPoint)) return;

            pulses[nextPulse] = new Pulse
            {
                point = localPoint,
                started = Time.unscaledTime,
                seed = NextSeed(),
                active = true
            };
            nextPulse = (nextPulse + 1) % pulses.Length;
            hasPulses = true;
            SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            System.Array.Clear(pulses, 0, pulses.Length);
            hasPulses = false;
            base.OnDisable();
        }

        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            hasPulses = false;
            float now = Time.unscaledTime;
            for (int i = 0; i < pulses.Length; i++)
            {
                if (!pulses[i].active) continue;
                float age = (now - pulses[i].started) / Lifetime;
                if (age >= 1f)
                {
                    pulses[i].active = false;
                    continue;
                }
                hasPulses = true;
                Draw(mesh, pulses[i], Mathf.Max(0, age));
            }
        }

        void Draw(UnityEngine.UI.VertexHelper mesh, Pulse pulse, float age)
        {
            float rotation = RandomValue(pulse.seed, 0) * 360f;
            var tint = color;
            Glow(mesh, pulse.point, 8f*SizeScale, Alpha(tint, Mathf.Max(0, 1f-age*5f)*Intensity*0.38f));
            int count = 4 + Mathf.FloorToInt(RandomValue(pulse.seed, 1)*5f);
            for (int i = 0; i < count; i++)
            {
                int k = 100 + i*17;
                float q = RandomValue(pulse.seed, k);
                float z = RandomValue(pulse.seed, k+1);
                float delay = RandomValue(pulse.seed, k+2)*0.065f;
                float u = Mathf.Clamp01((age-delay)/(1f-delay)*(1f+RandomValue(pulse.seed, k+3)*0.18f));
                if (age < delay || u >= 1f) continue;
                float ease = 1f-Mathf.Pow(1f-u, 2.4f);
                float alpha = Intensity*Mathf.Min(1f, u*32f+0.22f)*Mathf.Pow(1f-u, 0.85f+z*0.7f);
                float angle = rotation+(i+RandomValue(pulse.seed, k+4)*0.9f)*360f/count;
                Vector2 direction = Polar(angle);
                Vector2 point = pulse.point+direction*(3f+(19f+q*27f)*ease)*SizeScale;
                float size = (2.2f+z*2.5f)*SizeScale;
                float radius = size*(1.5f-u*0.8f);
                Color particle = Alpha(Color.Lerp(tint, Color.white, RandomValue(pulse.seed, k+6)*0.75f), alpha);
                Blob(mesh, point, radius, unchecked(pulse.seed+(uint)k), particle);
                if (i < 3)
                    Line(mesh, pulse.point+direction*(4f*SizeScale), point-direction*radius, size*0.55f, Alpha(tint, alpha*0.45f));
            }
        }

        uint NextSeed()
        {
            if (randomState == 0)
                randomState = unchecked((uint)System.Environment.TickCount ^ (uint)GetInstanceID()*2654435761u);
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            if (randomState == 0) randomState = 0x9e3779b9u;
            return randomState;
        }

        static float RandomValue(uint seed, int stream)
        {
            uint value = unchecked(seed+(uint)stream*0x9e3779b9u);
            value ^= value >> 16;
            value = unchecked(value*0x7feb352du);
            value ^= value >> 15;
            value = unchecked(value*0x846ca68bu);
            value ^= value >> 16;
            return (value & 0xffffff)/16777216f;
        }

        static Vector2 Polar(float degrees)
        {
            float radians = degrees*Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        static Color Alpha(Color value, float alpha)
        {
            value.a = Mathf.Clamp01(alpha);
            return value;
        }

        static void Vertex(UnityEngine.UI.VertexHelper mesh, Vector2 point, Color tint)
        {
            mesh.AddVert(new Vector3(point.x, point.y, 0), tint, Vector2.zero);
        }

        static void Blob(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, uint seed, Color tint)
        {
            int first = mesh.currentVertCount;
            Vertex(mesh, center, tint);
            for (int i = 0; i <= 10; i++)
                Vertex(mesh, center+Polar(i*36f)*radius*(0.7f+RandomValue(seed, i%10)*0.5f), tint);
            for (int i = 0; i < 10; i++) mesh.AddTriangle(first, first+i+1, first+i+2);
        }

        static void Line(UnityEngine.UI.VertexHelper mesh, Vector2 from, Vector2 to, float width, Color tint)
        {
            if (tint.a < 0.005f) return;
            Vector2 normal = new Vector2(from.y-to.y, to.x-from.x).normalized*width*0.5f;
            int first = mesh.currentVertCount;
            Vertex(mesh, from-normal, tint);
            Vertex(mesh, from+normal, tint);
            Vertex(mesh, to+normal, tint);
            Vertex(mesh, to-normal, tint);
            mesh.AddTriangle(first, first+1, first+2);
            mesh.AddTriangle(first, first+2, first+3);
        }

        static void Glow(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            if (tint.a < 0.005f) return;
            int first = mesh.currentVertCount;
            Vertex(mesh, center, tint);
            for (int i = 0; i <= 24; i++) Vertex(mesh, center+Polar(i*15f)*radius, Alpha(tint, 0));
            for (int i = 0; i < 24; i++) mesh.AddTriangle(first, first+i+1, first+i+2);
        }
    }
}
