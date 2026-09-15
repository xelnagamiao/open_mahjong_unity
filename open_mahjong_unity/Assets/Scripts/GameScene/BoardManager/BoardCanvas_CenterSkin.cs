using UnityEngine;

// Appearance only. The original serialized TMP/Image references and game-state
// methods remain in BoardCanvas.cs and BoardCanvas_CurrentDisplay.cs.
public partial class BoardCanvas
{
    CenterBoardSkin centerBoardSkin;

    public string CurrentCenterStyleId => centerBoardSkin ? centerBoardSkin.StyleId : "classic";

    public void ApplyCenterStyle(string id)
    {
        EnsureCenterBoardSkin().Apply(id);
    }

    public void ApplyCenterStyle(int index)
    {
        index = Mathf.Clamp(index, 0, CenterDisplayStyles.All.Count - 1);
        ApplyCenterStyle(CenterDisplayStyles.All[index].Id);
    }

    void Start()
    {
        EnsureCenterBoardSkin().BeginConfiguredAppearance();
    }

    void OnDestroy()
    {
        if (centerBoardSkin) centerBoardSkin.DetachConfiguration();
    }

    CenterBoardSkin EnsureCenterBoardSkin()
    {
        if (!centerBoardSkin)
        {
            centerBoardSkin = GetComponent<CenterBoardSkin>();
            if (!centerBoardSkin) centerBoardSkin = gameObject.AddComponent<CenterBoardSkin>();
            centerBoardSkin.Initialize(this, CurrentRoundText, remiansTilesText,
                new[] { player_self_score, player_right_score, player_top_score, player_left_score },
                new[] { player_self_index, player_right_index, player_top_index, player_left_index },
                new[] { player_self_current_image, player_right_current_image, player_top_current_image, player_left_current_image });
        }
        return centerBoardSkin;
    }
}
