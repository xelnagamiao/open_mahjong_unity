using System.Collections;
using UnityEngine;

public partial class ConfigManager {
    internal const string TileCornerLabelsKey = "TileCornerLabelsEnabled";
    internal const string TileCornerIntroKey = "TileCornerLabelsEnglishChoiceV1";
    public bool TileCornerLabelsEnabled { get; private set; }
    public bool HasChosenEnglishTileLabels => PlayerPrefs.GetInt(TileCornerIntroKey, 0) == 1;
    private Coroutine tileCornerIntroRoutine;

    public void SetTileCornerLabelsEnabled(bool enabled) {
        TileCornerLabelsEnabled = enabled;
        PlayerPrefs.SetInt(TileCornerLabelsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        TileCornerLabel.RefreshAll();
    }

    public void ConfirmEnglishTileLabels(bool enabled) {
        PlayerPrefs.SetInt(TileCornerIntroKey, 1);
        SetTileCornerLabelsEnabled(enabled);
    }

    public void DismissEnglishTileLabels() {
        PlayerPrefs.SetInt(TileCornerIntroKey, 1);
        PlayerPrefs.Save();
    }

    public static bool ShouldOfferTileLabels(AppLanguage language, bool chosen, bool authenticated, string window) =>
        language == AppLanguage.English && !chosen && authenticated && !string.IsNullOrEmpty(window) && window != "login";

    // Called by successful login only. Changing language never opens onboarding;
    // the first subsequent English login offers the choice after leaving login UI.
    public void OfferEnglishTileLabels() {
        if (!ShouldOfferTileLabels(CurrentLanguage, HasChosenEnglishTileLabels,
            UserDataManager.Instance != null && UserDataManager.Instance.UserId > 0,
            WindowsManager.Instance?.GetCurrentWindow()) || tileCornerIntroRoutine != null) return;
        tileCornerIntroRoutine = StartCoroutine(ShowTileCornerIntro());
    }

    private IEnumerator ShowTileCornerIntro() {
        yield return null;
        tileCornerIntroRoutine = null;
        if (ShouldOfferTileLabels(CurrentLanguage, HasChosenEnglishTileLabels,
            UserDataManager.Instance != null && UserDataManager.Instance.UserId > 0,
            WindowsManager.Instance?.GetCurrentWindow())) TileCornerLabelChoice.Show(this);
    }
}
