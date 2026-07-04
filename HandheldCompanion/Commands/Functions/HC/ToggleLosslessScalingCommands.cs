using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Misc;
using System;
using System.Threading;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class ToggleLosslessScalingCommands : FunctionCommands
    {
        private const string EnabledSetting = "LosslessScalingEnabled";
        private const string ExecutablePathSetting = "LosslessScalingExecutablePath";
        private const string ToggleHotkeySetting = "LosslessScalingToggleHotkey";
        private const string LaunchIfMissingSetting = "LosslessScalingLaunchIfMissing";
        private const string AllowPlayniteTargetSetting = "LosslessScalingAllowPlayniteTarget";
        private const string UseLastValidTargetSetting = "LosslessScalingUseLastValidTarget";
        private const string ShowToastOnNoTargetSetting = "LosslessScalingShowToastOnNoTarget";

        public ToggleLosslessScalingCommands()
        {
            Name = Properties.Resources.Hotkey_ToggleLosslessScaling;
            Description = Properties.Resources.Hotkey_ToggleLosslessScalingDesc;
            Glyph = "\uE7FC";
            OnKeyUp = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            if (!ManagerFactory.settingsManager.GetBoolean(EnabledSetting))
            {
                LogManager.LogInformation("[HandheldAlly] Lossless Scaling toggle skipped because the integration setting is disabled.");
                base.Execute(IsKeyDown, IsKeyUp, false);
                return;
            }

            string configuredHotkey = ManagerFactory.settingsManager.GetString(ToggleHotkeySetting);
            if (!HandheldAllyCommandUtils.TryParseHotkey(configuredHotkey, out VirtualKeyCode[] virtualKeys, out string normalizedHotkey))
            {
                LogManager.LogWarning("[HandheldAlly] Lossless Scaling toggle skipped because the configured hotkey is invalid: {0}", configuredHotkey);
                base.Execute(IsKeyDown, IsKeyUp, false);
                return;
            }

            string executablePath = ManagerFactory.settingsManager.GetString(ExecutablePathSetting);
            bool launchIfMissing = ManagerFactory.settingsManager.GetBoolean(LaunchIfMissingSetting);
            if (launchIfMissing && !HandheldAllyCommandUtils.IsLosslessScalingRunning() && !string.IsNullOrWhiteSpace(executablePath))
            {
                if (HandheldAllyCommandUtils.TryStartProcess(executablePath, string.Empty, out Exception? exception, System.Diagnostics.ProcessWindowStyle.Hidden))
                {
                    LogManager.LogInformation("[HandheldAlly] Launched Lossless Scaling before sending the toggle hotkey: {0}", executablePath);
                    Thread.Sleep(500);
                }
                else
                    LogManager.LogWarning("[HandheldAlly] Could not launch Lossless Scaling before toggling: path={0}, error={1}", executablePath, exception?.Message ?? "Unknown error");
            }

            bool allowPlayniteTarget = ManagerFactory.settingsManager.GetBoolean(AllowPlayniteTargetSetting);
            bool useLastValidTarget = ManagerFactory.settingsManager.GetBoolean(UseLastValidTargetSetting);
            bool showToastOnNoTarget = ManagerFactory.settingsManager.GetBoolean(ShowToastOnNoTargetSetting);

            if (!ControllerManager.TryGetLosslessScalingTarget(useLastValidTarget, allowPlayniteTarget, out ProcessEx? targetProcess, out string reason) || targetProcess is null)
            {
                LogManager.LogWarning("[HandheldAlly] Lossless Scaling toggle skipped: {0}. {1}", reason, ControllerManager.GetControllerFocusDiagnostics());

                if (showToastOnNoTarget)
                    ToastManager.SendToast(new ToastRequest
                    {
                        Title = Properties.Resources.LosslessScaling_ToastTitle,
                        Content = Properties.Resources.LosslessScaling_NoTargetToast,
                    });

                base.Execute(IsKeyDown, IsKeyUp, false);
                return;
            }

            bool quickToolsVisible = ControllerManager.IsQuickToolsVisible();
            if (quickToolsVisible && !HandheldAllyCommandUtils.TryBringProcessToForeground(targetProcess))
            {
                LogManager.LogWarning("[HandheldAlly] Lossless Scaling toggle skipped because the remembered target could not be restored to the foreground: {0}. {1}", targetProcess.Executable, ControllerManager.GetControllerFocusDiagnostics());
                base.Execute(IsKeyDown, IsKeyUp, false);
                return;
            }

            if (!quickToolsVisible)
                HandheldAllyCommandUtils.TryBringProcessToForeground(targetProcess);

            HandheldAllyCommandUtils.SendHotkey(virtualKeys);
            LogManager.LogInformation("[HandheldAlly] Sent Lossless Scaling hotkey {0} to target {1}. Reason={2}", normalizedHotkey, targetProcess.Executable, reason);

            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        public override object Clone()
        {
            ToggleLosslessScalingCommands commands = new()
            {
                commandType = commandType,
                Name = Name,
                Description = Description,
                Glyph = Glyph,
                OnKeyUp = OnKeyUp,
                OnKeyDown = OnKeyDown
            };

            return commands;
        }
    }
}
