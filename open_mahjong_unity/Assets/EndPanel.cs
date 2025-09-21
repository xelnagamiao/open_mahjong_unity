using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;


public class EndPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI FanTex1;
    [SerializeField] private TextMeshProUGUI FanTex2;
    [SerializeField] private TextMeshProUGUI FanTex3;
    [SerializeField] private TextMeshProUGUI FanTex4;
    [SerializeField] private TextMeshProUGUI FanTex5;
    [SerializeField] private TextMeshProUGUI FanTex6;
    [SerializeField] private TextMeshProUGUI FanTex7;
    [SerializeField] private TextMeshProUGUI FanTex8;
    [SerializeField] private TextMeshProUGUI FanTex9;
    [SerializeField] private TextMeshProUGUI FanTex10;
    [SerializeField] private TextMeshProUGUI FanTex11;
    [SerializeField] private TextMeshProUGUI FanTex12;
    [SerializeField] private TextMeshProUGUI SelfUserName;
    [SerializeField] private TextMeshProUGUI SelfPoint;
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftPoint;
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopPoint;
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightPoint;
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void ShowResult(int[] fan, int[] point, string selfUserName, int selfPoint, string leftUserName, int leftPoint, string topUserName, int topPoint, string rightUserName, int rightPoint)
    {
        FanTex1.text = fan[0].ToString();
        FanTex2.text = fan[1].ToString();
        FanTex3.text = fan[2].ToString();
        FanTex4.text = fan[3].ToString();
        FanTex5.text = fan[4].ToString();
    }
}
