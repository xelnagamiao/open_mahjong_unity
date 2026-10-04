using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MahjongClassroom {
    /// <summary>Scene-local lesson playback using the player's shared tile appearance settings.</summary>
    public sealed class CirnoClassroomController : MonoBehaviour, IPointerClickHandler {
        [Serializable]
        public sealed class GroupView {
            public GameObject root;
            public TextMeshProUGUI heading;
            public GameObject[] slots;
            public RectTransform[] cards;
            public UnityEngine.UI.Image[] backgrounds;
            public UnityEngine.UI.Image[] faces;
            public TextMeshProUGUI[] labels;

            public void Show(MahjongClassroomLesson lesson, MahjongClassroomLesson.TileGroup group) {
                root.SetActive(true);
                heading.text = group.heading;
                for (int i = 0; i < slots.Length; i++) {
                    bool visible = i < group.tileIds.Length;
                    slots[i].SetActive(visible);
                    if (!visible) continue;
                    var tile = lesson.FindTile(group.tileIds[i]);
                    labels[i].text = tile.label;
                    // Reserve the label below the card, then fit the selected body's actual aspect ratio.
                    var slot = (RectTransform)slots[i].transform;
                    Vector2 size = TileFaceFit.HandSizeFor(tile.id, slot.rect.width);
                    float availableHeight = Mathf.Max(1f, slot.rect.height - 32f);
                    if (size.y > availableHeight) size *= availableHeight / size.y;
                    cards[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                    TileFaceFit.ApplyHandLayers(cards[i], faces[i], ref backgrounds[i], tile.id);
                }
            }
        }

        [SerializeField] private MahjongClassroomLesson lesson;
        [SerializeField] private TextMeshProUGUI section;
        [SerializeField] private TextMeshProUGUI title;
        [SerializeField] private TextMeshProUGUI summary;
        [SerializeField] private TextMeshProUGUI narration;
        [SerializeField] private TextMeshProUGUI takeaway;
        [SerializeField] private TextMeshProUGUI pageNumber;
        [SerializeField] private TextMeshProUGUI advanceLabel;
        [SerializeField] private UnityEngine.UI.Image progress;
        [SerializeField] private UnityEngine.UI.Button previousButton;
        [SerializeField] private GroupView[] groups = Array.Empty<GroupView>();

        private int currentPage;
        private bool ready;
        private bool refreshing;
        public int CurrentPage => currentPage;
        public int PageCount => lesson != null ? lesson.PageCount : 0;

        private void Awake() {
            if (!ValidateSetup(out string problem)) {
                Debug.LogError("[Mahjong Classroom] " + problem, this);
                enabled = false;
                return;
            }
        }

        private void Start() {
            if (!enabled) return;
            // Main-scene entry reuses its configuration; direct scene playback loads the same saved settings.
            if (ConfigManager.Instance == null) {
                var settings = new GameObject("GlobalConfig");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(settings, gameObject.scene);
                settings.AddComponent<ConfigManager>();
            }
            ready = true;
            ShowPage(0);
            HandSurfaceLibrary.EnsureReady(RefreshAppearance);
        }

        private void OnEnable() {
            TileFaceResolver.OnPackChanged += RefreshAppearance;
            HandSurfaceLibrary.Changed += RefreshAppearance;
            RefreshAppearance();
        }

        private void OnDisable() {
            TileFaceResolver.OnPackChanged -= RefreshAppearance;
            HandSurfaceLibrary.Changed -= RefreshAppearance;
        }

        private void RefreshAppearance() {
            if (this != null && ready && isActiveAndEnabled && !refreshing) ShowPage(currentPage);
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (eventData.button == PointerEventData.InputButton.Left) Advance();
        }

        public void Advance() {
            if (!ready || !isActiveAndEnabled) return;
            ShowPage((currentPage + 1) % lesson.PageCount);
        }

        public void Previous() {
            if (!ready || !isActiveAndEnabled) return;
            ShowPage(Mathf.Max(0, currentPage - 1));
        }

        public void RestartLesson() {
            if (!ready || !isActiveAndEnabled) return;
            ShowPage(0);
        }

        private void ShowPage(int index) {
            refreshing = true;
            try {
                currentPage = index;
                var page = lesson.GetPage(index);
                section.text = page.section;
                title.text = page.title;
                summary.text = page.summary;
                narration.text = page.narration;
                takeaway.text = page.takeaway;
                pageNumber.text = $"{index + 1:00} / {lesson.PageCount:00}";
                advanceLabel.text = index == lesson.PageCount - 1 ? "重新开始  >" : "下一页  >";
                progress.fillAmount = (index + 1f) / lesson.PageCount;
                previousButton.interactable = index > 0;
                for (int i = 0; i < groups.Length; i++) {
                    if (i < page.groups.Length) groups[i].Show(lesson, page.groups[i]);
                    else groups[i].root.SetActive(false);
                }

                // Switching between one and three groups changes the stretched heading widths.
                // Rebuild on page changes so re-enabled TMP headings cannot retain the old geometry.
                if (groups.Length > 0 && groups[0].root.transform.parent is RectTransform container) {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(container);
                    foreach (var group in groups)
                        if (group.root.activeSelf) group.heading.ForceMeshUpdate();
                }
            } finally {
                refreshing = false;
            }
        }

        public bool ValidateSetup(out string problem) {
            problem = null;
            if (lesson == null || lesson.PageCount == 0) problem = "课程内容为空。";
            else if (section == null || title == null || summary == null || narration == null ||
                     takeaway == null || pageNumber == null || advanceLabel == null ||
                     progress == null || previousButton == null) problem = "课程 UI 引用不完整。";
            if (problem != null) return false;
            for (int p = 0; p < lesson.PageCount; p++) {
                var page = lesson.GetPage(p);
                if (page == null || page.groups == null || page.groups.Length > groups.Length) {
                    problem = $"第 {p + 1} 页的牌组配置无效。";
                    return false;
                }
                for (int g = 0; g < page.groups.Length; g++) {
                    var data = page.groups[g];
                    var view = groups[g];
                    if (data == null || data.tileIds == null || view == null || view.root == null ||
                        view.heading == null || view.slots == null || view.cards == null || view.backgrounds == null ||
                        view.faces == null || view.labels == null ||
                        data.tileIds.Length > view.slots.Length || view.faces.Length != view.slots.Length ||
                        view.labels.Length != view.slots.Length || view.cards.Length != view.slots.Length ||
                        view.backgrounds.Length != view.slots.Length) {
                        problem = $"第 {p + 1} 页的牌组视图容量或引用无效。";
                        return false;
                    }
                    for (int t = 0; t < data.tileIds.Length; t++) {
                        var tile = lesson.FindTile(data.tileIds[t]);
                        if (tile == null || !TilePackIds.IsStandardFaceId(tile.id) || view.slots[t] == null ||
                            view.cards[t] == null || view.backgrounds[t] == null || view.faces[t] == null || view.labels[t] == null) {
                            problem = $"第 {p + 1} 页缺少牌 {data.tileIds[t]} 的素材或视图。";
                            return false;
                        }
                    }
                }
            }
            return true;
        }

#if UNITY_EDITOR
        public void RefreshForEditor() {
            if (!ValidateSetup(out string problem)) throw new InvalidOperationException(problem);
            ShowPage(0);
        }

        public void ConfigureForEditor(MahjongClassroomLesson content, TextMeshProUGUI sectionText,
            TextMeshProUGUI titleText, TextMeshProUGUI summaryText, TextMeshProUGUI narrationText,
            TextMeshProUGUI takeawayText, TextMeshProUGUI counter, TextMeshProUGUI nextText,
            UnityEngine.UI.Image progressImage, UnityEngine.UI.Button previous, GroupView[] groupViews) {
            lesson = content;
            section = sectionText;
            title = titleText;
            summary = summaryText;
            narration = narrationText;
            takeaway = takeawayText;
            pageNumber = counter;
            advanceLabel = nextText;
            progress = progressImage;
            previousButton = previous;
            groups = groupViews;
            ShowPage(0);
        }
#endif
    }
}
