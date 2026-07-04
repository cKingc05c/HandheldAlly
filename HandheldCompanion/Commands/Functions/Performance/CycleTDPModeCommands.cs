using HandheldCompanion.Managers;
using HandheldCompanion.Shared;
using HandheldCompanion.Misc;
using HandheldCompanion.Processors;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HandheldCompanion.Commands.Functions.Performance
{
    [Serializable]
    public class CycleTDPModeCommands : FunctionCommands
    {
        private enum TdpCycleMode
        {
            Default = 0,
            LowPower = 1,
            Balanced = 2,
            Performance = 3,
            Turbo = 4,
        }

        private sealed class TdpSnapshot
        {
            public bool TdpOverrideEnabled { get; init; }
            public double[]? TdpOverrideValues { get; init; }
        }

        private static readonly TdpCycleMode[] CycleOrder =
        {
            TdpCycleMode.Default,
            TdpCycleMode.LowPower,
            TdpCycleMode.Balanced,
            TdpCycleMode.Performance,
            TdpCycleMode.Turbo,
        };

        private static readonly Dictionary<TdpCycleMode, double> TdpTargets = new()
        {
            [TdpCycleMode.LowPower] = 15.0d,
            [TdpCycleMode.Balanced] = 20.0d,
            [TdpCycleMode.Performance] = 25.0d,
            [TdpCycleMode.Turbo] = 30.0d,
        };

        private static readonly Dictionary<Guid, TdpSnapshot> Snapshots = [];

        public CycleTDPModeCommands()
        {
            Name = Properties.Resources.Hotkey_CycleTDPMode;
            Description = Properties.Resources.Hotkey_CycleTDPModeDesc;
            Glyph = "\uE945";
            OnKeyUp = true;
        }

        public override void Execute(bool IsKeyDown, bool IsKeyUp, bool IsBackground)
        {
            PowerProfile powerProfile = ManagerFactory.powerProfileManager.GetCurrent();
            if (powerProfile is null)
            {
                LogManager.LogWarning("[HandheldAlly] Cycle TDP Mode skipped because no power profile is currently active.");
                base.Execute(IsKeyDown, IsKeyUp, false);
                return;
            }

            TdpCycleMode currentMode = InferCurrentMode(powerProfile);
            if (currentMode == TdpCycleMode.Default)
                Snapshots[powerProfile.Guid] = CloneSnapshot(powerProfile);

            int currentIndex = Array.IndexOf(CycleOrder, currentMode);
            int nextIndex = (currentIndex + 1) % CycleOrder.Length;
            TdpCycleMode nextMode = CycleOrder[nextIndex];

            if (nextMode == TdpCycleMode.Default)
                RestoreSnapshot(powerProfile);
            else
                ApplyTargetMode(powerProfile, nextMode);

            ManagerFactory.powerProfileManager.UpdateOrCreateProfile(powerProfile, UpdateSource.Background);

            string appliedValues = powerProfile.TDPOverrideValues is null
                ? "n/a"
                : string.Join(", ", powerProfile.TDPOverrideValues.Select(value => value.ToString("0.#")));
            LogManager.LogInformation("[HandheldAlly] Cycle TDP Mode applied {0}. OverrideEnabled={1}, Values=[{2}]", GetDisplayName(nextMode), powerProfile.TDPOverrideEnabled, appliedValues);
            ToastManager.SendToast($"TDP mode set to {GetDisplayName(nextMode)}");

            base.Execute(IsKeyDown, IsKeyUp, false);
        }

        private static void ApplyTargetMode(PowerProfile powerProfile, TdpCycleMode mode)
        {
            double minimumTdp = PerformanceManager.GetMinimumTDP();
            double maximumTdp = PerformanceManager.GetMaximumTDP();
            double requestedTdp = TdpTargets.TryGetValue(mode, out double targetValue) ? targetValue : maximumTdp;
            double appliedTdp = Math.Max(minimumTdp, Math.Min(maximumTdp, requestedTdp));

            powerProfile.TDPOverrideEnabled = true;
            if (powerProfile.TDPOverrideValues is null || powerProfile.TDPOverrideValues.Length < 3)
                powerProfile.TDPOverrideValues = new[] { appliedTdp, appliedTdp, appliedTdp };
            else
            {
                for (int idx = (int)PowerType.Slow; idx <= (int)PowerType.Fast; idx++)
                    powerProfile.TDPOverrideValues[idx] = appliedTdp;
            }
        }

        private static void RestoreSnapshot(PowerProfile powerProfile)
        {
            if (!Snapshots.TryGetValue(powerProfile.Guid, out TdpSnapshot? snapshot))
            {
                powerProfile.TDPOverrideEnabled = powerProfile.TDPOverrideValues is not null;
                return;
            }

            powerProfile.TDPOverrideEnabled = snapshot.TdpOverrideEnabled;
            powerProfile.TDPOverrideValues = snapshot.TdpOverrideValues?.ToArray();
        }

        private static TdpCycleMode InferCurrentMode(PowerProfile powerProfile)
        {
            if (!powerProfile.TDPOverrideEnabled || powerProfile.TDPOverrideValues is null || powerProfile.TDPOverrideValues.Length == 0)
                return TdpCycleMode.Default;

            double currentTdp = powerProfile.TDPOverrideValues[(int)PowerType.Slow];
            foreach ((TdpCycleMode mode, double targetValue) in TdpTargets)
            {
                if (Math.Abs(currentTdp - targetValue) <= 0.75d)
                    return mode;
            }

            return TdpCycleMode.Default;
        }

        private static TdpSnapshot CloneSnapshot(PowerProfile powerProfile)
        {
            return new TdpSnapshot
            {
                TdpOverrideEnabled = powerProfile.TDPOverrideEnabled,
                TdpOverrideValues = powerProfile.TDPOverrideValues?.ToArray(),
            };
        }

        private static string GetDisplayName(TdpCycleMode mode)
        {
            return mode switch
            {
                TdpCycleMode.LowPower => "Low Power",
                TdpCycleMode.Balanced => "Balanced",
                TdpCycleMode.Performance => "Performance",
                TdpCycleMode.Turbo => "Turbo",
                _ => "Default",
            };
        }

        public override object Clone()
        {
            CycleTDPModeCommands commands = new()
            {
                commandType = commandType,
                Name = Name,
                Description = Description,
                Glyph = Glyph,
                OnKeyUp = OnKeyUp,
                OnKeyDown = OnKeyDown,
            };

            return commands;
        }
    }
}
