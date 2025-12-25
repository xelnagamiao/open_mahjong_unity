#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
#define USE_WINAPI
#endif

using System;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections;

public class FixedAspectRatio : MonoBehaviour
{
    public float targetAspectRatio = 16f / 9f; // 例如 1.777

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern System.IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(System.IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(
        System.IntPtr hWnd,
        System.IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    void Start()
    {
#if USE_WINAPI
        StartCoroutine(ApplyAspectRatioContinuously());
#endif
    }

    IEnumerator ApplyAspectRatioContinuously()
    {
        var wait = new WaitForSeconds(0.01f); // 100Hz
        System.IntPtr hwnd = GetActiveWindow();

        while (true)
        {
            yield return wait;

            if (GetWindowRect(hwnd, out RECT rect))
            {
                int width = rect.right - rect.left;
                int height = rect.bottom - rect.top;

                int targetWidth, targetHeight;
                float currentAspect = (float)width / height;

                if (currentAspect > targetAspectRatio)
                {
                    targetHeight = height;
                    targetWidth = Mathf.RoundToInt(targetHeight * targetAspectRatio);
                }
                else
                {
                    targetWidth = width;
                    targetHeight = Mathf.RoundToInt(targetWidth / targetAspectRatio);
                }

                // 避免微小抖动
                if (Mathf.Abs(width - targetWidth) > 2 || Mathf.Abs(height - targetHeight) > 2)
                {
                    // 0x0040 = SWP_NOZORDER | 0x0010 = SWP_NOACTIVATE → 合并为 0x0050?
                    // 实际常用组合：SWP_NOZORDER | SWP_NOACTIVATE = 0x0002 | 0x0010 = 0x0012
                    // 但更稳妥的是用 0x0002 (SWP_NOZORDER) + 0x0010 (SWP_NOACTIVATE) = 0x0012
                    const uint flags = 0x0002 | 0x0010; // SWP_NOZORDER | SWP_NOACTIVATE
                    SetWindowPos(hwnd, IntPtr.Zero, rect.left, rect.top, targetWidth, targetHeight, flags);
                }
            }
        }
    }
}