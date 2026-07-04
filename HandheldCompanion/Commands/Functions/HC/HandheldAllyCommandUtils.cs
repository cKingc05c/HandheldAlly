using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Inputs;
using HandheldCompanion.Misc;
using HandheldCompanion.Simulators;
using HandheldCompanion.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HandheldCompanion.Commands.Functions.HC
{
    internal static class HandheldAllyCommandUtils
    {
        private static readonly string[] DefaultFseExecutablePaths =
        {
            @"F:\Chris\AllyX\AnyFSE\AnyFSE.exe",
            @"C:\Program Files\AnyFSE\AnyFSE.exe",
        };

        public static string ResolveFseExecutablePath(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            string? fallback = DefaultFseExecutablePaths.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback;

            return !string.IsNullOrWhiteSpace(configuredPath)
                ? configuredPath
                : DefaultFseExecutablePaths[0];
        }

        public static bool TryStartProcess(string executablePath, string arguments, out Exception? exception, ProcessWindowStyle windowStyle = ProcessWindowStyle.Normal)
        {
            exception = null;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                exception = new FileNotFoundException("Executable path is empty.");
                return false;
            }

            if (!File.Exists(executablePath))
            {
                exception = new FileNotFoundException($"Executable was not found: {executablePath}", executablePath);
                return false;
            }

            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    WindowStyle = windowStyle,
                    UseShellExecute = true,
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                exception = ex;
                return false;
            }
        }

        public static bool TryParseHotkey(string configuredHotkey, out VirtualKeyCode[] virtualKeys, out string normalizedHotkey)
        {
            virtualKeys = [];
            normalizedHotkey = string.Empty;

            if (string.IsNullOrWhiteSpace(configuredHotkey))
                return false;

            List<VirtualKeyCode> keys = [];
            List<string> normalizedParts = [];
            string[] tokens = configuredHotkey
                .Split(new[] { '+', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string token in tokens)
            {
                if (Enum.TryParse<KeyFlags>(token, true, out KeyFlags keyFlag))
                {
                    keys.Add((VirtualKeyCode)keyFlag);
                    normalizedParts.Add(keyFlag.ToString());
                    continue;
                }

                if (Enum.TryParse<VirtualKeyCode>(token, true, out VirtualKeyCode virtualKeyCode))
                {
                    keys.Add(virtualKeyCode);
                    normalizedParts.Add(virtualKeyCode.ToString());
                    continue;
                }

                return false;
            }

            if (keys.Count == 0)
                return false;

            virtualKeys = keys.ToArray();
            normalizedHotkey = string.Join("+", normalizedParts);
            return true;
        }

        public static void SendHotkey(IReadOnlyList<VirtualKeyCode> keys)
        {
            foreach (VirtualKeyCode key in keys)
                KeyboardSimulator.KeyDown(key);

            for (int idx = keys.Count - 1; idx >= 0; idx--)
                KeyboardSimulator.KeyUp(keys[idx]);
        }

        public static bool IsLosslessScalingRunning()
        {
            return Process.GetProcessesByName("LosslessScaling").Length > 0;
        }

        public static bool TryBringProcessToForeground(ProcessEx processEx)
        {
            if (processEx is null)
                return false;

            try
            {
                processEx.Refresh(true);
            }
            catch { }

            IntPtr hWnd = processEx.Process?.MainWindowHandle ?? IntPtr.Zero;
            if (hWnd == IntPtr.Zero && processEx.ProcessWindows.Count > 0)
                hWnd = (IntPtr)processEx.ProcessWindows.Keys.First();

            if (hWnd == IntPtr.Zero)
                return false;

            ProcessUtils.ShowWindow(hWnd, (int)ProcessUtils.ShowWindowCommands.Restored);
            return ProcessUtils.SetForegroundWindow(hWnd);
        }
    }
}
