using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Destroyer : MonoBehaviour
{
    public static Destroyer Instance { get; private set; }
    
    [SerializeField] private Transform pendingDestroyContainer;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public void AddToDestroyer(Transform obj)
    {
        if (obj != null)
        {
            obj.SetParent(pendingDestroyContainer);
            Destroy(obj.gameObject);
        }
    }
    
    void Update()
    {
        // 清理已销毁对象的引用
        if (pendingDestroyContainer.childCount > 0)
        {
            for (int i = pendingDestroyContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = pendingDestroyContainer.GetChild(i);
                if (child == null)
                {
                    // 对象已被销毁，清理引用
                    // Unity会自动处理，这里可以添加额外的清理逻辑
                }
            }
        }
    }
}
