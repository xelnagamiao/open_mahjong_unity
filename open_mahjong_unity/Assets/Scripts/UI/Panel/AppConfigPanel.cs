using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AppConfigPanel : MonoBehaviour
{

    public static AppConfigPanel Instance;
    private void Awake()
    {
        Instance = this;
    }

}
