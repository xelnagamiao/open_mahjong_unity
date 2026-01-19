using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class FanCount : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI FanName;
    [SerializeField] private TextMeshProUGUI FanValue;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetFanCount(string name, int value){
        FanName.text = name;
        FanValue.text = value.ToString();
    }
}
