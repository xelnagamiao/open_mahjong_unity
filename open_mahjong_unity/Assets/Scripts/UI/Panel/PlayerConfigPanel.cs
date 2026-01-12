using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerConfigPanel : MonoBehaviour {
    public static PlayerConfigPanel Instance;
    private void Awake() {
        Instance = this;
    }
}
