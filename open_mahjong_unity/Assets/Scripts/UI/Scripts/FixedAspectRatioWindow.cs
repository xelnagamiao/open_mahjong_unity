#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define USE_WINAPI
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.UI;

/// <summary>
/// 固定宽高比窗口
/// </summary>
public class FixedAspectRatio : MonoBehaviour {
    public float targetAspectRatio = 16f / 9f; // 例如 1.777
    [SerializeField] private Camera[] targetCameras;

    private const string BlackBarCanvasName = "FixedAspectRatio Black Bars";
    /// <summary>最大化 letterbox 时需随相机视口裁剪的 Overlay 根 Canvas（还原后必须回到 Screen Space - Overlay）。</summary>
    private static readonly string[] LetterboxOverlayRootNames = { "OverlayCanvas", "MainCanvas", "#GameCanvas" };

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hWnd);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GWL_WNDPROC = -4;
    private const uint WM_SIZING = 0x0214;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    private IntPtr hwnd;
    private IntPtr originalWndProc;
    private WndProcDelegate wndProcDelegate;
    private RectTransform[] blackBars;
    private Canvas blackBarCanvas;
    private Rect currentCameraRect = new Rect(0f, 0f, 1f, 1f);
    private bool isLetterboxActive;
    private readonly Dictionary<Canvas, CanvasState> overlayCanvasStates = new Dictionary<Canvas, CanvasState>();
    private Canvas[] letterboxRootCanvases;

    private struct CanvasState {
        public RenderMode renderMode;
        public Camera worldCamera;
        public float planeDistance;
        public int sortingLayerID;
        public int sortingOrder;
    }

    void Start() {
#if USE_WINAPI
        hwnd = GetUnityWindowHandle();
        HookWindowProc();
#endif
        SetupBlackBars();
        CacheLetterboxRootCanvases();
        ApplyCameraViewport();
    }

    private void CacheLetterboxRootCanvases() {
        var roots = new List<Canvas>(LetterboxOverlayRootNames.Length);
        for (int i = 0; i < LetterboxOverlayRootNames.Length; i++) {
            GameObject root = GameObject.Find(LetterboxOverlayRootNames[i]);
            if (root == null) continue;
            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas != null && canvas != blackBarCanvas) {
                roots.Add(canvas);
            }
        }
        letterboxRootCanvases = roots.ToArray();
    }

    private void Update() {
        ApplyCameraViewport();
    }

    private void OnDestroy() {
#if USE_WINAPI
        UnhookWindowProc();
#endif
    }

    private void HookWindowProc() {
        wndProcDelegate = WindowProc;
        IntPtr newWndProc = Marshal.GetFunctionPointerForDelegate(wndProcDelegate);
        originalWndProc = SetWindowLongPtr(hwnd, GWL_WNDPROC, newWndProc);
    }

    private IntPtr GetUnityWindowHandle() {
        IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
        if (mainWindowHandle != IntPtr.Zero) {
            return mainWindowHandle;
        }

        return GetActiveWindow();
    }

    private void UnhookWindowProc() {
        if (originalWndProc != IntPtr.Zero) {
            SetWindowLongPtr(hwnd, GWL_WNDPROC, originalWndProc);
            originalWndProc = IntPtr.Zero;
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
        if (msg == WM_SIZING) {
            ApplyAspectRatioToSizingRect(wParam.ToInt32(), lParam);
            return new IntPtr(1);
        }

        return CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void ApplyAspectRatioToSizingRect(int sizingEdge, IntPtr rectPointer) {
        RECT rect = Marshal.PtrToStructure<RECT>(rectPointer);
        int width = GetRectWidth(rect);
        int height = GetRectHeight(rect);
        Vector2Int nonClientSize = GetNonClientSize();
        int clientWidth = Mathf.Max(1, width - nonClientSize.x);
        int clientHeight = Mathf.Max(1, height - nonClientSize.y);
        int targetWindowWidth = Mathf.RoundToInt(clientHeight * targetAspectRatio) + nonClientSize.x;
        int targetWindowHeight = Mathf.RoundToInt(clientWidth / targetAspectRatio) + nonClientSize.y;

        switch (sizingEdge) {
            case WMSZ_LEFT:
            case WMSZ_RIGHT:
                ResizeHeightFromCenter(ref rect, targetWindowHeight);
                break;
            case WMSZ_TOP:
            case WMSZ_BOTTOM:
                ResizeWidthFromCenter(ref rect, targetWindowWidth);
                break;
            case WMSZ_TOPLEFT:
                if (clientWidth > Mathf.RoundToInt(clientHeight * targetAspectRatio)) {
                    rect.left = rect.right - targetWindowWidth;
                } else {
                    rect.top = rect.bottom - targetWindowHeight;
                }
                break;
            case WMSZ_TOPRIGHT:
                if (clientWidth > Mathf.RoundToInt(clientHeight * targetAspectRatio)) {
                    rect.right = rect.left + targetWindowWidth;
                } else {
                    rect.top = rect.bottom - targetWindowHeight;
                }
                break;
            case WMSZ_BOTTOMLEFT:
                if (clientWidth > Mathf.RoundToInt(clientHeight * targetAspectRatio)) {
                    rect.left = rect.right - targetWindowWidth;
                } else {
                    rect.bottom = rect.top + targetWindowHeight;
                }
                break;
            case WMSZ_BOTTOMRIGHT:
                if (clientWidth > Mathf.RoundToInt(clientHeight * targetAspectRatio)) {
                    rect.right = rect.left + targetWindowWidth;
                } else {
                    rect.bottom = rect.top + targetWindowHeight;
                }
                break;
        }

        Marshal.StructureToPtr(rect, rectPointer, false);
    }

    private Vector2Int GetNonClientSize() {
        GetWindowRect(hwnd, out RECT windowRect);
        GetClientRect(hwnd, out RECT clientRect);
        int nonClientWidth = GetRectWidth(windowRect) - GetRectWidth(clientRect);
        int nonClientHeight = GetRectHeight(windowRect) - GetRectHeight(clientRect);
        return new Vector2Int(nonClientWidth, nonClientHeight);
    }

    private void SetupBlackBars() {
        GameObject canvasObject = new GameObject(BlackBarCanvasName);
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        blackBarCanvas = canvas;

        blackBars = new RectTransform[4];
        for (int i = 0; i < blackBars.Length; i++) {
            GameObject barObject = new GameObject($"Black Bar {i}");
            barObject.transform.SetParent(canvasObject.transform, false);
            Image image = barObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            blackBars[i] = image.rectTransform;
        }
    }

    private void ApplyCameraViewport() {
        bool shouldLetterbox = ShouldLetterbox();
        float aspect = Screen.width / (float)Screen.height;
        if (aspect < 1f) aspect = 1f / aspect;
        Rect targetRect = shouldLetterbox ? GetTargetViewport(aspect) : new Rect(0f, 0f, 1f, 1f);

        currentCameraRect = targetRect;
        Camera[] cameras = GetTargetCameras();
        for (int i = 0; i < cameras.Length; i++) {
            cameras[i].rect = currentCameraRect;
        }

        ApplyCanvasViewport(shouldLetterbox);
        ApplyBlackBars(currentCameraRect);
    }

    private void ApplyCanvasViewport(bool shouldLetterbox) {
        if (shouldLetterbox) {
            ActivateLetterboxCanvasMode();
            return;
        }

        DeactivateLetterboxCanvasMode();
        EnsureOverlayRootsUseOverlayMode();
    }

    private static CanvasState CaptureCanvasState(Canvas canvas) {
        return new CanvasState {
            renderMode = canvas.renderMode,
            worldCamera = canvas.worldCamera,
            planeDistance = canvas.planeDistance,
            sortingLayerID = canvas.sortingLayerID,
            sortingOrder = canvas.sortingOrder,
        };
    }

    private static void ApplyCanvasState(Canvas canvas, CanvasState state) {
        canvas.renderMode = state.renderMode;
        canvas.worldCamera = state.worldCamera;
        canvas.planeDistance = state.planeDistance;
        canvas.sortingLayerID = state.sortingLayerID;
        canvas.sortingOrder = state.sortingOrder;
    }

    private void ActivateLetterboxCanvasMode() {
        Camera canvasCamera = GetCanvasCamera();
        Canvas[] canvases = FindObjectsByType<Canvas>();
        for (int i = 0; i < canvases.Length; i++) {
            Canvas canvas = canvases[i];
            if (canvas == null || canvas == blackBarCanvas) continue;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

            if (!overlayCanvasStates.ContainsKey(canvas)) {
                overlayCanvasStates.Add(canvas, CaptureCanvasState(canvas));
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = canvasCamera;
        }

        isLetterboxActive = true;
    }

    private void DeactivateLetterboxCanvasMode() {
        if (!isLetterboxActive && overlayCanvasStates.Count == 0) return;

        foreach (KeyValuePair<Canvas, CanvasState> pair in overlayCanvasStates) {
            if (pair.Key == null) continue;
            ApplyCanvasState(pair.Key, pair.Value);
        }

        overlayCanvasStates.Clear();
        isLetterboxActive = false;
    }

    /// <summary>
    /// 窗口还原后若 Canvas 仍卡在 Screen Space - Camera，Main(Overlay) 会整层盖住 Overlay(Camera)。
    /// 每帧在非最大化状态下强制根 Canvas 回到 Overlay 模式，修复最大化→还原后的「混合态」。
    /// </summary>
    private void EnsureOverlayRootsUseOverlayMode() {
        if (letterboxRootCanvases == null || letterboxRootCanvases.Length == 0) {
            CacheLetterboxRootCanvases();
        }

        for (int i = 0; i < letterboxRootCanvases.Length; i++) {
            Canvas canvas = letterboxRootCanvases[i];
            if (canvas == null) {
                CacheLetterboxRootCanvases();
                return;
            }
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) continue;

            if (overlayCanvasStates.TryGetValue(canvas, out CanvasState saved)) {
                ApplyCanvasState(canvas, saved);
                continue;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
        }
    }

    private void ApplyBlackBars(Rect targetRect) {
        SetBarAnchors(blackBars[0], new Vector2(0f, 0f), new Vector2(targetRect.xMin, 1f));
        SetBarAnchors(blackBars[1], new Vector2(targetRect.xMax, 0f), new Vector2(1f, 1f));
        SetBarAnchors(blackBars[2], new Vector2(targetRect.xMin, 0f), new Vector2(targetRect.xMax, targetRect.yMin));
        SetBarAnchors(blackBars[3], new Vector2(targetRect.xMin, targetRect.yMax), new Vector2(targetRect.xMax, 1f));
    }

    private void SetBarAnchors(RectTransform bar, Vector2 anchorMin, Vector2 anchorMax) {
        bar.anchorMin = anchorMin;
        bar.anchorMax = anchorMax;
        bar.offsetMin = Vector2.zero;
        bar.offsetMax = Vector2.zero;
    }

    private Camera[] GetTargetCameras() {
        if (targetCameras != null && targetCameras.Length > 0) {
            return targetCameras;
        }

        return Camera.allCameras;
    }

    private Camera GetCanvasCamera() {
        if (targetCameras != null && targetCameras.Length > 0) {
            return targetCameras[0];
        }

        return Camera.main;
    }

    private Rect GetTargetViewport(float screenAspectRatio) {
        if (screenAspectRatio > targetAspectRatio) {
            float width = targetAspectRatio / screenAspectRatio;
            return new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }

        float height = screenAspectRatio / targetAspectRatio;
        return new Rect(0f, (1f - height) * 0.5f, 1f, height);
    }

    private int GetRectWidth(RECT rect) {
        return rect.right - rect.left;
    }

    private int GetRectHeight(RECT rect) {
        return rect.bottom - rect.top;
    }

    private bool ShouldLetterbox() {
#if USE_WINAPI
        return IsZoomed(hwnd);
#elif UNITY_ANDROID || UNITY_IOS
        return true;
#else
        return false;
#endif
    }

    private void ResizeHeightFromCenter(ref RECT rect, int targetHeight) {
        int centerY = (rect.top + rect.bottom) / 2;
        rect.top = centerY - targetHeight / 2;
        rect.bottom = rect.top + targetHeight;
    }

    private void ResizeWidthFromCenter(ref RECT rect, int targetWidth) {
        int centerX = (rect.left + rect.right) / 2;
        rect.left = centerX - targetWidth / 2;
        rect.right = rect.left + targetWidth;
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) {
        if (IntPtr.Size == 8) {
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }

        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }
}