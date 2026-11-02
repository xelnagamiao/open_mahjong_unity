using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Salasasa.Home
{
    public sealed class LobbyShowcase : MonoBehaviour
    {
        [System.NonSerialized] public System.Action<LobbyAction> actionHandler;
        public event System.Action ArtworkChanged;
        public GameObject[] pages;
        public LobbyTheme[] themes;
        public Canvas canvas;
        public Camera uiCamera;
        public LobbyShape background;
        public UnityEngine.UI.RawImage backgroundImage;
        public GameObject characterLayer;
        public LobbySilhouette silhouette;
        public UnityEngine.UI.RawImage characterImage;
        public Texture2D characterArtwork;
        public Texture2D backgroundArtwork;
        public LobbyArtwork[] artworkSets;
        public LobbyArtwork[] backgroundSets;
        public LobbyArtwork[] characterSets;
        public bool independentArtworkSelection;
        public int selectedArtworkIndex;
        public int selectedBackgroundIndex;
        public LobbyShape artworkShade;
        public bool artworkEnabled = true;
        public LobbyOverlay overlay;
        public GameObject previewControls;
        public TMP_Text themeLabel;
        public int initialTheme;
        public bool showCharacterFill = true;
        public bool transitions = true;
        public int CurrentTheme { get; private set; }
        public LobbyTheme Theme => themes[CurrentTheme];
        public int ArtworkCount => artworkSets == null ? 0 : artworkSets.Length;
        public int CharacterCount => independentArtworkSelection && characterSets != null && characterSets.Length > 0 ? characterSets.Length : ArtworkCount;
        public LobbyArtwork CharacterAt(int index) => CharacterCount == 0 ? null : (independentArtworkSelection && characterSets != null && characterSets.Length > 0 ? characterSets : artworkSets)[Mathf.Clamp(index, 0, CharacterCount - 1)];
        public LobbyArtwork CurrentArtwork => CharacterAt(independentArtworkSelection ? selectedArtworkIndex : CurrentTheme);
        public int BackgroundCount => independentArtworkSelection && backgroundSets != null && backgroundSets.Length > 0 ? backgroundSets.Length : ArtworkCount;
        public LobbyArtwork BackgroundAt(int index) => BackgroundCount == 0 ? null : (independentArtworkSelection && backgroundSets != null && backgroundSets.Length > 0 ? backgroundSets : artworkSets)[Mathf.Clamp(index, 0, BackgroundCount - 1)];
        public LobbyArtwork CurrentBackgroundArtwork => BackgroundAt(independentArtworkSelection ? selectedBackgroundIndex : CurrentTheme);
        public bool HasCharacterArtwork => artworkEnabled && (characterArtwork != null || CurrentArtwork?.character != null);
        Coroutine fade;
        readonly Dictionary<UnityEngine.UI.Button, UnityEngine.Events.UnityAction> actionBindings = new Dictionary<UnityEngine.UI.Button, UnityEngine.Events.UnityAction>();
        readonly Dictionary<int, int> expressionChoices = new Dictionary<int, int>();
        Texture2D expressionTexture;
        string expressionResource;
        public int ExpressionCount => Mathf.Max(1, CurrentArtwork?.expressions?.Length ?? 0);
        public int CurrentExpressionIndex => expressionChoices.TryGetValue(selectedArtworkIndex, out int choice) ? Mathf.Clamp(choice, 0, ExpressionCount - 1) : 0;
        public LobbyExpression CurrentExpression => CurrentArtwork?.expressions != null && CurrentArtwork.expressions.Length > 0 ? CurrentArtwork.expressions[CurrentExpressionIndex] : null;
        public string ExpressionName => CurrentExpression?.name ?? "默认";

        void Awake()
        {
#if UNITY_EDITOR
            if (CompleteLobbyEnvironment.IsDevelopment) DevelopmentArtworkCatalog.LoadInto(this);
#endif
            Application.targetFrameRate = 60;
            BindActions(gameObject);
            foreach (var page in pages) BindActions(page);
            BindActions(previewControls);
            if (overlay != null) BindActions(overlay.gameObject);
            int initial = initialTheme;
            string[] args = CompleteLobbyEnvironment.IsDevelopment ? System.Environment.GetCommandLineArgs() : System.Array.Empty<string>();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-theme" && int.TryParse(args[i + 1], out int parsed)) initial = parsed - 1;
                if (independentArtworkSelection && args[i] == "-art" && int.TryParse(args[i + 1], out int art)) SetArtworkPairIndices(art - 1);
            }
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (independentArtworkSelection && args[i] == "-background" && int.TryParse(args[i + 1], out int bg))
                    selectedBackgroundIndex = Mathf.Clamp(bg - 1, 0, BackgroundCount - 1);
                if (independentArtworkSelection && args[i] == "-character" && int.TryParse(args[i + 1], out int character))
                    selectedArtworkIndex = Mathf.Clamp(character - 1, 0, CharacterCount - 1);
            }
            SelectTheme(initial, false);
            RefreshArtwork();
        }

        void Update()
        {
            if (!CompleteLobbyEnvironment.IsDevelopment) return;
            var key = Keyboard.current;
            if (key == null) return;
            bool editing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
            if (independentArtworkSelection)
            {
                if (!editing && ArtworkCount > 0)
                {
                    if (key.digit1Key.wasPressedThisFrame) SelectArtworkPair(0);
                    if (key.digit2Key.wasPressedThisFrame) SelectArtworkPair(1);
                    if (key.digit3Key.wasPressedThisFrame) SelectArtworkPair(2);
                    if (key.digit4Key.wasPressedThisFrame) SelectArtworkPair(3);
                    if (key.digit5Key.wasPressedThisFrame) SelectArtworkPair(4);
                    if (key.f9Key.wasPressedThisFrame) CycleBackground(-1);
                    if (key.f10Key.wasPressedThisFrame) CycleBackground(1);
                    if (key.f11Key.wasPressedThisFrame) CycleCharacter(-1);
                    if (key.f12Key.wasPressedThisFrame) CycleCharacter(1);
                }
            }
            else
            {
                if (key.f1Key.wasPressedThisFrame || (!editing && key.digit1Key.wasPressedThisFrame)) SelectTheme(0);
                if (key.f2Key.wasPressedThisFrame || (!editing && key.digit2Key.wasPressedThisFrame)) SelectTheme(1);
                if (key.f3Key.wasPressedThisFrame || (!editing && key.digit3Key.wasPressedThisFrame)) SelectTheme(2);
                if (key.f4Key.wasPressedThisFrame || (!editing && key.digit4Key.wasPressedThisFrame)) SelectTheme(3);
                if (key.f5Key.wasPressedThisFrame || (!editing && key.digit5Key.wasPressedThisFrame)) SelectTheme(4);
            }
            if (key.f6Key.wasPressedThisFrame && actionHandler != null)
            {
                var visibility = FindFirstObjectByType<HomeUiVisibility>();
                if (visibility && !visibility.IsHidden) visibility.Toggle();
            }
            if (key.f7Key.wasPressedThisFrame) previewControls.SetActive(!previewControls.activeSelf);
            if (key.f8Key.wasPressedThisFrame) ToggleArtwork();
            if (!editing && key.tabKey.wasPressedThisFrame) { if (overlay.IsOpen) overlay.Close(); else overlay.Open("appearance", "界面方案"); }
            if (key.escapeKey.wasPressedThisFrame) overlay.Close();
        }

        public void SelectTheme(int index, bool animate = true)
        {
            CurrentTheme = Mathf.Clamp(index, 0, pages.Length - 1);
            if (fade != null) StopCoroutine(fade);
            for (int i = 0; i < pages.Length; i++)
            {
                pages[i].SetActive(i == CurrentTheme);
                pages[i].GetComponent<CanvasGroup>().alpha = 1;
            }
            background.color = Theme.background; background.lowerColor = Theme.backgroundBottom; background.SetVerticesDirty();
            silhouette.color = Theme.silhouette;
            if (themeLabel != null) themeLabel.text = Theme.name + "  ·  " + Theme.reference.Split('·')[0].Trim();
            if (overlay != null) overlay.Close();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            RefreshArtwork();
            if (animate && transitions && Application.isPlaying) fade = StartCoroutine(Fade(pages[CurrentTheme].GetComponent<CanvasGroup>()));
        }

        public void ToggleCharacter() { showCharacterFill = !showCharacterFill; RefreshArtwork(); }
        public void ToggleArtwork() { artworkEnabled = !artworkEnabled; RefreshArtwork(); }
        public void SelectArtworkPair(int index)
        {
            if (!CompleteLobbyEnvironment.IsDevelopment) return;
            if (ArtworkCount == 0) return;
            SetArtworkPairIndices(index);
            artworkEnabled = true; showCharacterFill = true;
            RefreshArtwork();
            if (overlay != null) overlay.RefreshArtworkSelection();
        }
        void SetArtworkPairIndices(int index)
        {
            if (ArtworkCount == 0) return;
            var pair = artworkSets[Mathf.Clamp(index, 0, ArtworkCount - 1)];
            if (independentArtworkSelection)
            {
                selectedArtworkIndex = Mathf.Max(0, System.Array.FindIndex(characterSets, art => art.character == pair.character));
                selectedBackgroundIndex = Mathf.Max(0, System.Array.FindIndex(backgroundSets, art => art.background == pair.background));
            }
            else selectedArtworkIndex = selectedBackgroundIndex = Mathf.Clamp(index, 0, ArtworkCount - 1);
        }
        public void SelectCharacter(int index, bool closePicker = false)
        {
            if (!CompleteLobbyEnvironment.IsDevelopment) return;
            if (CharacterCount == 0) return;
            selectedArtworkIndex = Mathf.Clamp(index, 0, CharacterCount - 1);
            if (closePicker && overlay != null) overlay.Close();
            artworkEnabled = true; RefreshArtwork();
            if (overlay != null) overlay.RefreshArtworkSelection();
        }
        public void CycleCharacter(int direction = 1)
        {
            if (CharacterCount == 0) return;
            SelectCharacter(((selectedArtworkIndex + direction) % CharacterCount + CharacterCount) % CharacterCount, false);
        }
        public void SelectBackground(int index, bool closePicker = false)
        {
            if (!CompleteLobbyEnvironment.IsDevelopment) return;
            if (BackgroundCount == 0) return;
            selectedBackgroundIndex = Mathf.Clamp(index, 0, BackgroundCount - 1);
            if (closePicker && overlay != null) overlay.Close();
            artworkEnabled = true; RefreshArtwork();
            if (overlay != null) overlay.RefreshArtworkSelection();
        }
        public void CycleBackground(int direction = 1)
        {
            if (BackgroundCount == 0) return;
            SelectBackground(((selectedBackgroundIndex + direction) % BackgroundCount + BackgroundCount) % BackgroundCount, false);
        }
        public void SelectExpression(int index)
        {
            if (!CompleteLobbyEnvironment.IsDevelopment) return;
            expressionChoices[selectedArtworkIndex] = Mathf.Clamp(index, 0, ExpressionCount - 1);
            RefreshArtwork();
            if (overlay != null) overlay.RefreshArtworkSelection();
        }
        public void CycleExpression(int direction = 1)
        {
            if (ExpressionCount < 2) return;
            SelectExpression(((CurrentExpressionIndex + direction) % ExpressionCount + ExpressionCount) % ExpressionCount);
        }
        public void RefreshArtwork()
        {
            if (characterLayer == null || characterImage == null || silhouette == null) return;
            var art = CurrentArtwork;
            var backgroundArt = CurrentBackgroundArtwork;
            string resource = artworkEnabled && characterArtwork == null ? CurrentExpression?.resourcePath : null;
            Texture2D previousExpression = null;
            if (expressionResource != resource)
            {
                previousExpression = expressionTexture;
                expressionTexture = string.IsNullOrEmpty(resource) ? null : Resources.Load<Texture2D>(resource);
                expressionResource = resource;
                if (!string.IsNullOrEmpty(resource) && expressionTexture == null) Debug.LogError("Missing expression: " + resource);
            }
            Texture2D figure = artworkEnabled ? characterArtwork != null ? characterArtwork : expressionTexture != null ? expressionTexture : art?.character : null;
            Texture2D backdrop = artworkEnabled ? backgroundArtwork != null ? backgroundArtwork : backgroundArt?.background : null;
            characterLayer.SetActive(showCharacterFill);
            silhouette.gameObject.SetActive(figure == null);
            characterImage.texture = figure;
            if (previousExpression != null && previousExpression != expressionTexture) Resources.UnloadAsset(previousExpression);
            characterImage.color = figure != null ? Color.white : Color.clear;
            characterImage.uvRect = characterArtwork == null && art != null ? art.characterUV : new Rect(0, 0, 1, 1);
            if (figure != null)
            {
                var r = characterImage.rectTransform;
                Vector2 pixels = new Vector2(figure.width * Mathf.Abs(characterImage.uvRect.width), figure.height * Mathf.Abs(characterImage.uvRect.height));
                float maxWidth = art != null ? art.characterMaxWidth : 800;
                float height = art != null ? art.characterHeight : 800;
                float scale = Mathf.Min(maxWidth / Mathf.Max(1, pixels.x), height / Mathf.Max(1, pixels.y));
                r.sizeDelta = pixels * scale;
                r.anchorMin = r.anchorMax = new Vector2(.5f, 0); r.pivot = new Vector2(.5f, 0);
                r.anchoredPosition = art != null ? art.characterOffset : Vector2.zero;
            }
            if (backgroundImage != null)
            {
                backgroundImage.texture = backdrop;
                backgroundImage.color = backdrop != null ? backgroundArt != null ? backgroundArt.backgroundTint : Color.white : Color.clear;
                var fitter = backgroundImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                if (fitter != null && backdrop != null) fitter.aspectRatio = (float)backdrop.width / backdrop.height;
            }
            if (artworkShade != null)
            {
                artworkShade.gameObject.SetActive(backdrop != null && backgroundArt != null);
                if (backgroundArt != null) { artworkShade.color = backgroundArt.shadeLeft; artworkShade.lowerColor = backgroundArt.shadeRight; artworkShade.SetVerticesDirty(); }
            }
            ArtworkChanged?.Invoke();
        }

        // Pages and overlays may live on separate canvases; bind their authored roots explicitly.
        public void BindActions(GameObject root)
        {
            if (root == null) return;
            foreach (var link in root.GetComponentsInChildren<LobbyAction>(true))
            {
                var button = link.GetComponent<UnityEngine.UI.Button>();
                if (button == null || actionBindings.ContainsKey(button)) continue;
                UnityEngine.Events.UnityAction listener = () => Act(link);
                actionBindings.Add(button, listener);
                button.onClick.AddListener(listener);
            }
        }

        void OnDestroy()
        {
            foreach (var binding in actionBindings)
                if (binding.Key != null) binding.Key.onClick.RemoveListener(binding.Value);
            actionBindings.Clear();
            if (expressionTexture != null) Resources.UnloadAsset(expressionTexture);
        }

        public void Act(LobbyAction link)
        {
            actionHandler?.Invoke(link);
        }

        IEnumerator Fade(CanvasGroup group)
        {
            float elapsed = 0;
            while (elapsed < .14f) { elapsed += Time.unscaledDeltaTime; group.alpha = Mathf.Clamp01(elapsed / .14f); yield return null; }
            group.alpha = 1; fade = null;
        }
    }
}
