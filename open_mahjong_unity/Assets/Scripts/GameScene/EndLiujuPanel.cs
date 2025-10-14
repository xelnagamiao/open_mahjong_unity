using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EndLiujuPanel : MonoBehaviour
{
    public static EndLiujuPanel Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void ShowLiujuPanel(){
        gameObject.SetActive(true);
        StartCoroutine(AutoHideAfterDelay());
    }

    public void ClearEndLiujuPanel(){
        gameObject.SetActive(false);
    }

    private IEnumerator AutoHideAfterDelay(){
        yield return new WaitForSeconds(2f);
        gameObject.SetActive(false);
    }
}
