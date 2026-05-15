#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define USE_WINAPI
#endif

using System;
using System.Diagnostics;
using UnityEngine;
using System.Runtime.InteropServices;

/// <summary>
/// 固定宽高比窗口
/// </summary>
public class FixedAspectRatio : MonoBehaviour {
    public float targetAspectRatio = 16f / 9f; // 例如 1.777

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

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

    void Start() {
#if USE_WINAPI
        hwnd = GetUnityWindowHandle();
        HookWindowProc();
#endif
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
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        int targetWidth = Mathf.RoundToInt(height * targetAspectRatio);
        int targetHeight = Mathf.RoundToInt(width / targetAspectRatio);

        switch (sizingEdge) {
            case WMSZ_LEFT:
            case WMSZ_RIGHT:
                ResizeHeightFromCenter(ref rect, targetHeight);
                break;
            case WMSZ_TOP:
            case WMSZ_BOTTOM:
                ResizeWidthFromCenter(ref rect, targetWidth);
                break;
            case WMSZ_TOPLEFT:
                if (width > targetWidth) {
                    rect.left = rect.right - targetWidth;
                } else {
                    rect.top = rect.bottom - targetHeight;
                }
                break;
            case WMSZ_TOPRIGHT:
                if (width > targetWidth) {
                    rect.right = rect.left + targetWidth;
                } else {
                    rect.top = rect.bottom - targetHeight;
                }
                break;
            case WMSZ_BOTTOMLEFT:
                if (width > targetWidth) {
                    rect.left = rect.right - targetWidth;
                } else {
                    rect.bottom = rect.top + targetHeight;
                }
                break;
            case WMSZ_BOTTOMRIGHT:
                if (width > targetWidth) {
                    rect.right = rect.left + targetWidth;
                } else {
                    rect.bottom = rect.top + targetHeight;
                }
                break;
        }

        Marshal.StructureToPtr(rect, rectPointer, false);
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