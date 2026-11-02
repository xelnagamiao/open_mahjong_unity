using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Salasasa.Preview
{
    public sealed class PreviewWindowTitle : MonoBehaviour
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const string Argument = "-preview-title";
        private string title;

        [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowText(IntPtr window, string value);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            string value = ReadTitle(Environment.GetCommandLineArgs());
            if (string.IsNullOrEmpty(value))
                return;

            var root = new GameObject(nameof(PreviewWindowTitle));
            DontDestroyOnLoad(root);
            root.AddComponent<PreviewWindowTitle>().title = value;
        }

        private IEnumerator Start()
        {
            var process = Process.GetCurrentProcess();
            var delay = new WaitForSecondsRealtime(0.25f);
            int successfulAttempts = 0;
            IntPtr lastWindow = IntPtr.Zero;
            try
            {
                for (int attempt = 0; attempt < 40 && successfulAttempts < 5; attempt++)
                {
                    IntPtr window = TrySetTitle(process, title);
                    if (window != IntPtr.Zero)
                    {
                        successfulAttempts = window == lastWindow ? successfulAttempts + 1 : 1;
                        lastWindow = window;
                    }
                    else
                    {
                        successfulAttempts = 0;
                    }
                    if (successfulAttempts < 5)
                        yield return delay;
                }
            }
            finally
            {
                process.Dispose();
                Destroy(gameObject);
            }
        }

        private static IntPtr TrySetTitle(Process process, string value)
        {
            try
            {
                process.Refresh();
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero)
                    return IntPtr.Zero;

                GetWindowThreadProcessId(window, out uint owner);
                if (owner != (uint)process.Id)
                    return IntPtr.Zero;

                return SetWindowText(window, value) ? window : IntPtr.Zero;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return IntPtr.Zero;
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }
        }

        private static string ReadTitle(string[] args)
        {
            string value = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (string.Equals(args[i], Argument, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        value = args[++i];
                }
                else if (args[i].StartsWith(Argument + "=", StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i].Substring(Argument.Length + 1);
                }
            }
            if (value == null)
                return null;

            value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ').Trim();
            return value.Length > 128 ? value.Substring(0, 128) : value;
        }
#endif
    }
}

