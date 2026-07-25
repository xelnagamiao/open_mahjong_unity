using UnityEngine;

public class Destroyer : MonoBehaviour {
    public static Destroyer Instance { get; private set; }

    [SerializeField] private Transform pendingDestroyContainer;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
        }
    }

    public void AddToDestroyer(Transform obj) {
        if (obj != null) {
            obj.SetParent(pendingDestroyContainer);
            Destroy(obj.gameObject);
        }
    }
}
