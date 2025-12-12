using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameRecordManager : MonoBehaviour
{
    // Start is called before the first frame update
    public static GameRecordManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    else{
            Destroy(gameObject);
        }
    }

    public void LoadRecord(string recordJson){
        Debug.Log("加载游戏记录: " + recordJson);
    }

    public void HideGameRecord(){
        gameObject.SetActive(false);
    }
}
