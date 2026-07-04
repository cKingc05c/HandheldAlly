using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using System;

namespace HandheldCompanion.Commands.Functions.HC
{
    [Serializable]
    public class ReenterFSECommands : FunctionCommands
    {
        private const string FseExecutablePathSetting = "FSEExecutablePath";
        private const string FseArgumentsSetting = "FSEArguments";

        public ReenterFSECommands()
        {
            Name = Properties.Resources.Hotkey_ReenterFSE;
            Description = Properties.Resources.Hotkey_ReenterFSEDesc;
            Glyph = "\uE8A7";
            OnKeyUp = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            string configuredPath = ManagerFactory.settingsManager.GetString(FseExecutablePathSetting);
            string executablePath = HandheldAllyCommandUtils.ResolveFseExecutablePath(configuredPath);
            string arguments = ManagerFactory.settingsManager.GetString(FseArgumentsSetting);
            if (string.IsNullOrWhiteSpace(arguments))
                arguments = "/FSE";

            if (HandheldAllyCommandUtils.TryStartProcess(executablePath, arguments, out Exception? exception))
                LogManager.LogInformation("[HandheldAlly] Reenter FSE launched: {0} {1}", executablePath, arguments);
            else
                LogManager.LogError("[HandheldAlly] Failed to launch Reenter FSE command: path={0}, args={1}, error={2}", executablePath, arguments, exception?.Message ?? "Unknown error");

            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        public override object Clone()
        {
            ReenterFSECommands commands = new()
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
