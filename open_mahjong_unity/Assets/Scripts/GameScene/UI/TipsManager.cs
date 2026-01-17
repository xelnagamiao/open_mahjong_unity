using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TipsManager : MonoBehaviour
{
    [SerializeField] private GameObject TileContainer;
    [SerializeField] private GameObject TilePrefab;
    [SerializeField] private GameObject FanPrefab;
    [SerializeField] private GameObject FanContainer;
    public static TipsManager Instance { get; private set; }
    public List<int> waitingTiles = new List<int>();
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
