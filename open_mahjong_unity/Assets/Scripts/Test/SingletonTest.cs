using UnityEngine;
using UnityEngine.SceneManagement;

public class SingletonTest : MonoBehaviour
{
    public static SingletonTest Instance { get; private set; }
    
    [Header("测试设置")]
    [SerializeField] private bool useDontDestroyOnLoad = true;
    
    private void Awake()
    {
        Debug.Log($"[SingletonTest] Awake called on {gameObject.name}");
        
        if (Instance == null)
        {
            Instance = this;
            Debug.Log($"[SingletonTest] Instance set to {gameObject.name}");
            
            if (useDontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
                Debug.Log($"[SingletonTest] DontDestroyOnLoad applied to {gameObject.name}");
            }
        }
        else
        {
            Debug.Log($"[SingletonTest] Duplicate found, destroying {gameObject.name}");
            Destroy(gameObject);
        }
    }
    
    private void OnDestroy()
    {
        Debug.Log($"[SingletonTest] OnDestroy called on {gameObject.name}");
        
        if (Instance == this)
        {
            Debug.Log($"[SingletonTest] Instance is being destroyed!");
            Instance = null;
        }
    }
    
    public void TestMethod()
    {
        Debug.Log($"[SingletonTest] TestMethod called from {gameObject.name}");
    }
    
    // 测试按钮
    [ContextMenu("Test Singleton Access")]
    public void TestSingletonAccess()
    {
        if (Instance != null)
        {
            Debug.Log($"[SingletonTest] Singleton access successful: {Instance.gameObject.name}");
            Instance.TestMethod();
        }
        else
        {
            Debug.LogError("[SingletonTest] Singleton access failed - Instance is null!");
        }
    }
    
    [ContextMenu("Load New Scene")]
    public void LoadNewScene()
    {
        Debug.Log("[SingletonTest] Loading new scene...");
        SceneManager.LoadScene("TestScene2");
    }
} 