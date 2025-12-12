using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameConfigPanel : MonoBehaviour
{

    public static GameConfigPanel Instance;
    private void Awake()
    {
        Instance = this;
    }

}
