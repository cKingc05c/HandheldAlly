using GregsStack.InputSimulatorStandard.Native;
using HandheldCompanion.Actions;
using HandheldCompanion.Controllers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using HandheldCompanion.Properties;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace HandheldCompanion.ViewModels
{
    public class ButtonMappingViewModel : MappingViewModel
    {
        private static HashSet<MouseActionsType> _unsupportedMouseActionTypes =
        [
            MouseActionsType.Move,
            MouseActionsType.Scroll,
        ];

        #region Mapping Properties

        private int _pressTypeFallbackIndex = 0;
        public override int PressTypeIndex
        {
            get => Action is not null ? (int)Action.pressType : 0;
            set
            {
                // In case press type is changed when there's no action yet
                // Keep track of value to set PressTypeIndex when action is created
                _pressTypeFallbackIndex = value;

                if (Action is not null && value != PressTypeIndex)
                {
                    Action.pressType = (PressType)value;
                    OnPropertyChanged(nameof(PressTypeIndex));
                    OnPropertyChanged(nameof(PressTypeTooltip));
                    OnPropertyChanged(nameof(HasDuration));
                }
            }
        }

        public override string PressTypeTooltip
        {
            get
            {
                string key = $"LayoutPage_PressTypeTooltip{PressTypeIndex}";
                return Resources.ResourceManager.GetString(key) ?? string.Empty;
            }
        }

        public override float LongPressDelay
        {
            get => Action is not null ? Action.ActionTimer : 0;
            set
            {
                if (Action is not null && value != LongPressDelay)
                {
                    Action.ActionTimer = value;
                    OnPropertyChanged(nameof(LongPressDelay));
                }
            }
        }

        public override int ModifierIndex
        {
            get
            {
                if (Action is KeyboardActions keyboardAction)
                    return (int)keyboardAction.Modifiers;

                if (Action is MouseActions mouseAction)
                    return (int)mouseAction.Modifiers;

                return 0;
            }

            set
            {
                if (Action is not null && value != ModifierIndex)
                {
                    if (Action is KeyboardActions keyboardAction)
                        keyboardAction.Modifiers = (ModifierSet)value;

                    else if (Action is MouseActions mouseAction)
                        mouseAction.Modifiers = (ModifierSet)value;

                    OnPropertyChanged(nameof(ModifierIndex));
                }
            }
        }

        public override bool HasModifier
        {
            get
            {
                if (Action is not null)
                {
                    if (Action is KeyboardActions keyboardAction)
                    {
                        return true;
                    }
                    else if (Action is MouseActions mouseActions)
                    {
                        switch (mouseActions.MouseType)
                        {
                            case MouseActionsType.LeftButton:
                            case MouseActionsType.RightButton:
                            case MouseActionsType.MiddleButton:
                            case MouseActionsType.ScrollUp:
                            case MouseActionsType.ScrollDown:
                                return true;
                            case MouseActionsType.MoveTo:
                                return false;
                        }
                    }
                }

                return false;
            }
        }

        public override float TriggerOutput
        {
            get => (Action is TriggerActions triggerAction) ? triggerAction.motionThreshold : 0;
            set
            {
                if (Action is TriggerActions triggerAction && value != TriggerOutput)
                {
                    triggerAction.motionThreshold = value;
                    OnPropertyChanged(nameof(TriggerOutput));
                }
            }
        }

        // Trigger output should only be visible for Button -> Trigger mappings
        public override Visibility TriggerOutputVisibility
        {
            get
            {
                ActionType currentActionType = (ActionType)ActionTypeIndex;
                return currentActionType == ActionType.Trigger ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public override int HapticModeIndex
        {
            get => Action is not null ? (int)Action.HapticMode : 0;
            set
            {
                if (Action is not null && value != HapticModeIndex)
                {
                    Action.HapticMode = (HapticMode)value;
                    OnPropertyChanged(nameof(HapticModeIndex));
                }
            }
        }

        public override int HapticStrengthIndex
        {
            get => Action is not null ? (int)Action.HapticStrength : 0;
            set
            {
                if (Action is not null && value != HapticStrengthIndex)
                {
                    Action.HapticStrength = (HapticStrength)value;
                    OnPropertyChanged(nameof(HapticStrengthIndex));
                }
            }
        }

        public override double Button2MouseToX
        {
            get => (Action is MouseActions mouseAction) ? mouseAction.MoveToX : 0;
            set
            {
                if (Action is MouseActions mouseAction && value != Button2MouseToX)
                {
                    mouseAction.MoveToX = value is double.NaN ? 0 : value;
                    OnPropertyChanged(nameof(Button2MouseToX));
                }
            }
        }

        public override double Button2MouseToY
        {
            get => (Action is MouseActions mouseAction) ? mouseAction.MoveToY : 0;
            set
            {
                if (Action is MouseActions mouseAction && value != Button2MouseToY)
                {
                    mouseAction.MoveToY = value is double.NaN ? 0 : value;
                    OnPropertyChanged(nameof(Button2MouseToY));
                }
            }
        }

        public override bool Button2MouseRestore
        {
            get => (Action is MouseActions mouseAction) ? mouseAction.MoveToPrevious : false;
            set
            {
                if (Action is MouseActions mouseAction && value != Button2MouseRestore)
                {
                    mouseAction.MoveToPrevious = value;
                    OnPropertyChanged(nameof(Button2MouseRestore));
                }
            }
        }

        public override Visibility Button2MouseTo
        {
            get
            {
                ActionType currentActionType = (ActionType)ActionTypeIndex;
                if (currentActionType == ActionType.Mouse && SelectedTarget != null)
                {
                    if (SelectedTarget.Tag is not MouseActionsType mouseAction)
                        return Visibility.Collapsed;

                    return mouseAction == MouseActionsType.MoveTo ? Visibility.Visible : Visibility.Collapsed;
                }

                return Visibility.Collapsed;
            }
        }

        public override void OnPropertyChanged(string? propertyName)
        {
            switch (propertyName)
            {
                case "":
                case nameof(SelectedTarget):
                case nameof(ActionTypeIndex):
                    OnPropertyChanged(nameof(HasModifier));
                    OnPropertyChanged(nameof(Button2MouseTo));
                    OnPropertyChanged(nameof(GeneralActionVisibility));
                    break;
            }

            base.OnPropertyChanged(propertyName);
        }

        public override bool HasDuration => PressTypeIndex != (int)PressType.Short;

        #endregion

        private ButtonStackViewModel _parentStack;
        public ButtonStackViewModel ParentStack => _parentStack;

        public ICommand ButtonCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }

        public ButtonMappingViewModel(ButtonStackViewModel parentStack, ButtonFlags button) : base(button)
        {
            _parentStack = parentStack;

            ButtonCommand = new DelegateCommand(() =>
            {
                if (Action is not null) Delete();
                _parentStack.RemoveMapping(this);
            });

            OpenSettingsCommand = new DelegateCommand(() =>
            {
                // Navigate to LayoutItemPage
                if (MainWindow.layoutItemPage is not null)
                {
                    MainWindow.layoutItemPage.SetMapping(this);
                    MainWindow.NavView_Navigate(MainWindow.layoutItemPage);
                }
            });
        }

        protected override void ActionTypeChanged(ActionType? newActionType = null)
        {
            var actionType = newActionType ?? (ActionType)ActionTypeIndex;
            if (actionType == ActionType.Disabled)
            {
                if (Action is not null) Delete();
                SelectedTarget = null;
                OnPropertyChanged(string.Empty);
                return;
            }

            // get current controller
            IController controller = ControllerManager.GetDefault(true);

            // Build Targets
            List<MappingTargetViewModel> targets = new List<MappingTargetViewModel>();

            PressType fallbackPressType = (PressType)_pressTypeFallbackIndex;

            if (actionType == ActionType.Button)
            {
                if (Action is null || Action is not ButtonActions)
                {
                    Action = new ButtonActions()
                    {
                        pressType = fallbackPressType,
                        ShiftSlot = ShiftSlot.Any,
                        ShiftMatchAny = false
                    };
                }

                MappingTargetViewModel? matchingTargetVm = null;
                foreach (var button in controller.GetTargetButtons())
                {
                    var mappingTargetVm = new MappingTargetViewModel
                    {
                        Tag = button,
                        Content = controller.GetButtonName(button)
                    };
                    targets.Add(mappingTargetVm);

                    if (button == ((ButtonActions)Action).Button)
                        matchingTargetVm = mappingTargetVm;
                }

                lock (_collectionLock)
                {
                    Targets.Clear();
                    foreach (var t in targets)
                        Targets.Add(t);
                }
                SelectedTarget = matchingTargetVm ?? Targets.First();
            }
            else if (actionType == ActionType.Keyboard)
            {
                if (Action is null || Action is not KeyboardActions)
                {
                    Action = new KeyboardActions()
                    {
                        pressType = fallbackPressType,
                        Modifiers = ModifierSet.None,
                        ShiftSlot = ShiftSlot.Any,
                        ShiftMatchAny = false
                    };
                }

                lock (_collectionLock)
                {
                    Targets.Clear();
                    foreach (var t in _keyboardKeysTargets)
                        Targets.Add(t);
                }
                SelectedTarget = _keyboardKeysTargets.FirstOrDefault(e => Equals(e.Tag, ((KeyboardActions)Action).Key)) ?? _keyboardKeysTargets.First();
            }
            else if (actionType == ActionType.Mouse)
            {
                if (Action is null || Action is not MouseActions)
                {
                    Action = new MouseActions()
                    {
                        pressType = fallbackPressType,
                        Modifiers = ModifierSet.None,
                        ShiftSlot = ShiftSlot.Any,
                        ShiftMatchAny = false
                    };
                }

                MappingTargetViewModel? matchingTargetVm = null;
                foreach (var mouseType in Enum.GetValues<MouseActionsType>().Except(_unsupportedMouseActionTypes))
                {
                    var mappingTargetVm = new MappingTargetViewModel
                    {
                        Tag = mouseType,
                        Content = EnumUtils.GetDescriptionFromEnumValue(mouseType)
                    };
                    targets.Add(mappingTargetVm);

                    if (mouseType == ((MouseActions)Action).MouseType)
                        matchingTargetVm = mappingTargetVm;
                }

                // Update list and selected target
                lock (_collectionLock)
                {
                    Targets.Clear();
                    foreach (var t in targets)
                        Targets.Add(t);
                }
                SelectedTarget = matchingTargetVm ?? Targets.First();
            }
            else if (actionType == ActionType.Trigger)
            {
                if (Action is null || Action is not TriggerActions)
                {
                    Action = new TriggerActions()
                    {
                        motionThreshold = 125,
                        ShiftSlot = ShiftSlot.Any,
                        ShiftMatchAny = false
                    };
                }

                MappingTargetViewModel? matchingTargetVm = null;
                foreach (var axis in controller.GetTargetTriggers())
                {
                    var mappingTargetVm = new MappingTargetViewModel
                    {
                        Tag = axis,
                        Content = controller.GetAxisName(axis)
                    };
                    targets.Add(mappingTargetVm);

                    if (axis == ((TriggerActions)Action).Axis)
                        matchingTargetVm = mappingTargetVm;
                }

                lock (_collectionLock)
                {
                    Targets.Clear();
                    foreach (var t in targets)
                        Targets.Add(t);
                }
                SelectedTarget = matchingTargetVm ?? Targets.First();
            }
            else if (actionType == ActionType.Shift)
            {
                if (Action is null || Action is not ShiftActions)
                    Action = new ShiftActions();

                MappingTargetViewModel? matchingTargetVm = null;
                // Only show individual shift slots (A, B, C, D), not None or combined values
                foreach (ShiftSlot shiftSlot in new[] { ShiftSlot.ShiftA, ShiftSlot.ShiftB, ShiftSlot.ShiftC, ShiftSlot.ShiftD })
                {
                    MappingTargetViewModel mappingTargetVm = new MappingTargetViewModel
                    {
                        Tag = shiftSlot,
                        Content = EnumUtils.GetDescriptionFromEnumValue(shiftSlot)
                    };
                    targets.Add(mappingTargetVm);

                    if (shiftSlot == ((ShiftActions)Action).ActivationSlot)
                        matchingTargetVm = mappingTargetVm;
                }

                // Update list and selected target
                lock (_collectionLock)
                {
                    Targets.Clear();
                    foreach (var t in targets)
                        Targets.Add(t);
                }
                SelectedTarget = matchingTargetVm ?? Targets.First();
            }
            else if (actionType == ActionType.Inherit)
            {
                if (Action is null || Action is not InheritActions)
                    Action = new InheritActions();

                // Update list and selected target
                Targets.Clear();
            }

            // Refresh mapping
            OnPropertyChanged(string.Empty);
        }

        protected override void TargetTypeChanged()
        {
            if (Action is null || SelectedTarget is null)
                return;

            switch (Action.actionType)
            {
                case ActionType.Button:
                    if (SelectedTarget.Tag is ButtonFlags buttonFlags)
                        ((ButtonActions)Action).Button = buttonFlags;
                    break;

                case ActionType.Keyboard:
                    if (SelectedTarget.Tag is VirtualKeyCode virtualKeyCode)
                        ((KeyboardActions)Action).Key = virtualKeyCode;
                    break;

                case ActionType.Mouse:
                    if (SelectedTarget.Tag is MouseActionsType mouseActionsType)
                        ((MouseActions)Action).MouseType = mouseActionsType;
                    break;

                case ActionType.Shift:
                    if (SelectedTarget.Tag is ShiftSlot shiftSlot)
                        ((ShiftActions)Action).ActivationSlot = shiftSlot;
                    break;

                case ActionType.Trigger:
                    if (SelectedTarget.Tag is AxisLayoutFlags axisLayoutFlags)
                        ((TriggerActions)Action).Axis = axisLayoutFlags;
                    break;
            }
        }

        protected override void Update()
        {
            _parentStack.UpdateFromMapping();
        }

        protected override void Delete()
        {
            Action = null;
            _parentStack.UpdateFromMapping();
        }

        // Done from ButtonStack
        protected override void UpdateMapping(Layout layout)
        {
        }
    }
}
