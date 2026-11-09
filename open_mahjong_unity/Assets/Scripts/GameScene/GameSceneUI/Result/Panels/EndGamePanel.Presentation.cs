using TMPro;
using UnityEngine;

public partial class EndGamePanel {
    static string FinalScoreText(object rawScore) {
        decimal score = System.Convert.ToDecimal(rawScore, System.Globalization.CultureInfo.InvariantCulture);
        return score.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string RatingPointMarkup(string value) => value.EndsWith(" PT", System.StringComparison.Ordinal)
        ? value.Substring(0, value.Length - 2) + "<color=#78BDE8>pt</color>" : value;

    void SetVerificationText(string masterSeed, string commitment, string salt) {
        TableText.Set(gameRandomSeed, "主种子：\n" + masterSeed
            + "\n\n承诺：\n" + CommitmentSaltDisplay.NormalizeCommitment(commitment)
            + "\n\n盐：\n" + (string.IsNullOrEmpty(salt) ? "-" : salt));
        var scroll = gameRandomSeed.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll) {
            scroll.StopMovement();
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.verticalNormalizedPosition = 1;
        }
    }
}
