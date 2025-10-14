using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class SwitchSeatPanel : MonoBehaviour
{
    public static SwitchSeatPanel Instance { get; private set; }
    [SerializeField] private TMP_Text NextRoundName;
    [SerializeField] private TMP_Text TopText;
    [SerializeField] private TMP_Text SelfText;
    [SerializeField] private TMP_Text LeftText;
    [SerializeField] private TMP_Text RightText;

    // 存储四个方向text的原始Rect位置
    private Vector3 originalTopPosition;
    private Vector3 originalSelfPosition;
    private Vector3 originalLeftPosition;
    private Vector3 originalRightPosition;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // 存储原始Rect位置
        originalTopPosition = TopText.rectTransform.anchoredPosition;
        originalSelfPosition = SelfText.rectTransform.anchoredPosition;
        originalLeftPosition = LeftText.rectTransform.anchoredPosition;
        originalRightPosition = RightText.rectTransform.anchoredPosition;
    }

    // Start is called before the first frame update
    public void ShowSwitchSeatPanel(int gameRound, Dictionary<int, string> indexToPosition){
        int selfIndex = GameSceneManager.Instance.selfIndex;
        
        gameObject.SetActive(true);

        if (gameRound == 5){
            NextRoundName.text = "东圈=>南圈";
        }
        else if (gameRound == 9){
            NextRoundName.text = "南圈=>西圈";
        }
        else if (gameRound == 13){
            NextRoundName.text = "西圈=>北圈";
        }

        if (gameRound == 5 || gameRound == 13){
            // 东位与南位互换，西位与北位互换
            if (selfIndex == 0){
                SelfText.text = "南";    // 东→南
                RightText.text = "东";   // 南→东
                TopText.text = "北";     // 西→北
                LeftText.text = "西";    // 北→西
            }
            else if (selfIndex == 1){
                SelfText.text = "东";    // 南→东
                RightText.text = "北";   // 西→北
                TopText.text = "西";     // 北→西
                LeftText.text = "南";    // 东→南
            }
            else if (selfIndex == 2){
                SelfText.text = "北";    // 西→北
                RightText.text = "西";   // 北→西
                TopText.text = "南";     // 东→南
                LeftText.text = "东";    // 南→东
            }
            else if (selfIndex == 3){
                SelfText.text = "西";    // 北→西
                RightText.text = "南";   // 东→南
                TopText.text = "东";     // 南→东
                LeftText.text = "北";    // 西→北
            }
        }
        else if (gameRound == 9){
            // 东位到西位，南位到北位，西位到南位，北位到东位
            if (selfIndex == 0){
                SelfText.text = "西";  // 东 = 西
                RightText.text = "北"; // 南 = 北
                TopText.text = "南";   // 西 = 南
                LeftText.text = "东";  // 北 = 东
            }
            else if (selfIndex == 1){
                SelfText.text = "北"; // 南 = 北
                RightText.text = "南"; // 西 = 南
                TopText.text = "东"; // 北 = 东
                LeftText.text = "西";  // 东 = 西
            }
            else if (selfIndex == 2){
                SelfText.text = "南"; // 西 = 南
                RightText.text = "东"; // 北 = 东
                TopText.text = "西"; // 东 = 西
                LeftText.text = "北"; // 南 = 北
            }
            else if (selfIndex == 3){
                SelfText.text = "东"; // 北 = 东
                RightText.text = "西"; // 东 = 西
                TopText.text = "北"; // 南 = 北
                LeftText.text = "南"; // 西 = 南
            }
        }
        // 启动移动动画
        StartCoroutine(SwitchSeatCoroutine(gameRound));
    }

    public void ClearSwitchSeatPanel(){
        // 将UI元素还原到原始位置
        TopText.rectTransform.anchoredPosition = originalTopPosition;
        SelfText.rectTransform.anchoredPosition = originalSelfPosition;
        LeftText.rectTransform.anchoredPosition = originalLeftPosition;
        RightText.rectTransform.anchoredPosition = originalRightPosition;
        
        gameObject.SetActive(false);
    }

    private IEnumerator SwitchSeatCoroutine(int gameRound){
        // 公共变量设置
        TMP_Text eastText = null;
        TMP_Text southText = null;
        TMP_Text westText = null;
        TMP_Text northText = null;
        
        // 找到东、南、西、北四个文本
        TMP_Text[] allTexts = {TopText, SelfText, LeftText, RightText};
        foreach (TMP_Text text in allTexts){
            if (text.text == "东") eastText = text;
            else if (text.text == "南") southText = text;
            else if (text.text == "西") westText = text;
            else if (text.text == "北") northText = text;
        }
        
        const float duration = 0.5f;
        
        if (gameRound == 5 || gameRound == 13){
            // 东位与南位互换，西位与北位互换
            yield return StartCoroutine(SwapPositions(eastText, southText, duration));
            yield return StartCoroutine(SwapPositions(westText, northText, duration));
        }
        else if (gameRound == 9){
            // 东位到西位，南位到北位，西位到南位，北位到东位
            yield return StartCoroutine(MoveToPosition(eastText, westText, duration));
            yield return StartCoroutine(MoveToPosition(southText, northText, duration));
            yield return StartCoroutine(MoveToPosition(westText, southText, duration));
            yield return StartCoroutine(MoveToPosition(northText, eastText, duration));
        }
    }
    
    // 互换文本
    private IEnumerator SwapPositions(TMP_Text text1, TMP_Text text2, float duration){
        // 获取两个文本的位置
        Vector3 pos1 = text1.rectTransform.anchoredPosition;
        Vector3 pos2 = text2.rectTransform.anchoredPosition;
        
        float elapsedTime = 0f;
        while (elapsedTime < duration){
            // 按时间百分比计算插值
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;
            
            // Lerp(起始位置, 目标位置, 进度)
            text1.rectTransform.anchoredPosition = Vector3.Lerp(pos1, pos2, progress);
            text2.rectTransform.anchoredPosition = Vector3.Lerp(pos2, pos1, progress);
            yield return null;
        }

    }
    
    // 将文本移动到目标位置
    private IEnumerator MoveToPosition(TMP_Text text, TMP_Text targetText, float duration){

        // 获取起始位置和目标位置
        Vector3 startPos = text.rectTransform.anchoredPosition;
        Vector3 targetPos = targetText.rectTransform.anchoredPosition;
        
        float elapsedTime = 0f;
        while (elapsedTime < duration){
            // 按时间百分比计算插值
            elapsedTime += Time.deltaTime;
            float progress = elapsedTime / duration;
            // Lerp(起始位置, 目标位置, 进度)
            text.rectTransform.anchoredPosition = Vector3.Lerp(startPos, targetPos, progress);
            yield return null;
        }
        
    }
}
