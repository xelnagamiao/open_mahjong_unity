using System;
using UnityEngine;

namespace Salasasa.Home
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(InventoryPanel))]
    public sealed class InventoryLayoutVariants : MonoBehaviour
    {
        public const string PreferenceKey = "SalasasaInventory.Layout.V1";
        public static readonly string[] VariantNames = { "紧凑居中" };

        [Serializable]
        public struct RectPose
        {
            public RectTransform target;
            public Vector2 anchorMin, anchorMax, pivot, position, size;
            public Vector3 scale;

            public void Apply()
            {
                if (!target) return;
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.pivot = pivot;
                target.anchoredPosition = position;
                target.sizeDelta = size;
                target.localScale = scale;
            }
        }

        [Serializable]
        public struct Tint
        {
            public UnityEngine.UI.Graphic target;
            public Color color;
        }

        [Serializable]
        public sealed class LayoutSpec
        {
            public string name;
            public Vector2 panelSize;
            public int columns;
            public bool fullscreenBackground;
            public Color backdropColor;
            public RectPose[] rects;
            public Tint[] tints;
        }

        [SerializeField] InventoryPanel panel;
        [SerializeField] UnityEngine.UI.Image fullscreenBackdrop;
        [SerializeField] UnityEngine.UI.ScrollRect listScroll, detailScroll;
        [SerializeField] UnityEngine.UI.GridLayoutGroup grid;
        [SerializeField] LayoutSpec[] variants;
        [SerializeField] int selectedVariant;
        bool selectionLoaded, preferencesDirty;
        Vector2 lastParentSize;

        public InventoryPanel Panel => panel;
        public RectTransform Frame => (RectTransform)transform;
        public UnityEngine.UI.Image FullscreenBackdrop => fullscreenBackdrop;
        public UnityEngine.UI.ScrollRect ListScroll => listScroll;
        public UnityEngine.UI.ScrollRect DetailScroll => detailScroll;
        public UnityEngine.UI.GridLayoutGroup Grid => grid;
        public int Variant => selectedVariant;
        public int VariantCount => variants == null ? 0 : variants.Length;
        public Vector2 CurrentPanelSize => Ready ? variants[selectedVariant].panelSize : Vector2.zero;
        public float AppliedScale => transform.localScale.x;
        public LayoutSpec[] BakedVariants => variants;
        bool Ready => panel && fullscreenBackdrop && listScroll && detailScroll && grid && variants != null && variants.Length == 1;

        void Awake() { LoadSelection(); }
        void OnEnable()
        {
            if (!Application.isPlaying || !Ready) return;
            LoadSelection();
            ApplyCurrent(false);
        }

        void LateUpdate()
        {
            if (!Ready) return;
            var parent = transform.parent as RectTransform;
            if (parent && (parent.rect.size - lastParentSize).sqrMagnitude > .01f) FitToParent();
        }

        void LoadSelection()
        {
            if (selectionLoaded || !Ready || !Application.isPlaying) return;
            selectionLoaded = true;
            selectedVariant = 0;
        }

        public void SelectVariant(int index)
        {
            if (!Ready) throw new InvalidOperationException("Inventory layout variants have not been baked.");
            selectedVariant = 0;
            selectionLoaded = true;
            ApplyCurrent(true);
            if (Application.isPlaying)
            {
                PlayerPrefs.SetInt(PreferenceKey, selectedVariant);
                preferencesDirty = true;
            }
        }

        public void ConfigureBaked(InventoryPanel inventory, UnityEngine.UI.Image backdrop,
            UnityEngine.UI.ScrollRect items, UnityEngine.UI.ScrollRect description,
            UnityEngine.UI.GridLayoutGroup itemGrid, LayoutSpec[] layouts, int initialVariant = 0)
        {
            panel = inventory;
            fullscreenBackdrop = backdrop;
            listScroll = items;
            detailScroll = description;
            grid = itemGrid;
            variants = new[] { layouts[0] };
            selectedVariant = 0;
            if (!Ready) throw new InvalidOperationException("Inventory layout references are incomplete.");
            ApplyCurrent(false);
        }

        void ApplyCurrent(bool retainScroll)
        {
            var layout = variants[selectedVariant];
            float itemsPosition = listScroll.verticalNormalizedPosition;
            float detailsPosition = detailScroll.verticalNormalizedPosition;
            foreach (var pose in layout.rects) pose.Apply();
            foreach (var tint in layout.tints) if (tint.target) tint.target.color = tint.color;
            if (TryGetComponent<InventoryPalette>(out var palette)) palette.ApplyCurrent();
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = layout.columns;
            fullscreenBackdrop.color = layout.backdropColor;
            fullscreenBackdrop.gameObject.SetActive(layout.fullscreenBackground);
            fullscreenBackdrop.transform.SetAsFirstSibling();
            FitToParent();
            UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(listScroll.content);
            if (Application.isPlaying && listScroll.gameObject.activeInHierarchy && retainScroll)
            {
                listScroll.StopMovement();
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(listScroll.content);
                listScroll.verticalNormalizedPosition = itemsPosition;
                detailScroll.verticalNormalizedPosition = detailsPosition;
            }
        }

        void FitToParent()
        {
            var parent = transform.parent as RectTransform;
            Vector2 available = parent ? parent.rect.size : new Vector2(1920, 1080);
            if (available.x < 1 || available.y < 1) available = new Vector2(1920, 1080);
            var size = variants[selectedVariant].panelSize;
            float referenceScale = .9f * Mathf.Min(available.x / 1600f, available.y / 900f);
            float scale = Mathf.Min(referenceScale, Mathf.Min(available.x / (size.x + 32), available.y / (size.y + 32)));
            scale = Mathf.Max(.01f, scale);
            Frame.anchorMin = Frame.anchorMax = Frame.pivot = new Vector2(.5f, .5f);
            Frame.anchoredPosition = Vector2.zero;
            Frame.sizeDelta = size;
            Frame.localScale = Vector3.one * scale;
            var backdrop = fullscreenBackdrop.rectTransform;
            backdrop.anchorMin = backdrop.anchorMax = backdrop.pivot = new Vector2(.5f, .5f);
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.localScale = Vector3.one;
            // A child backdrop reaches the parent viewport without reparenting any business control.
            backdrop.sizeDelta = available / scale;
            lastParentSize = available;
        }

        void SavePreferences()
        {
            if (!preferencesDirty) return;
            PlayerPrefs.Save();
            preferencesDirty = false;
        }
        void OnDisable() { SavePreferences(); }
        void OnApplicationPause(bool paused) { if (paused) SavePreferences(); }
        void OnApplicationQuit() { SavePreferences(); }
    }
}
