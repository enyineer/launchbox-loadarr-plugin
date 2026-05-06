using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Loadarr.Services;

namespace Loadarr.UI
{
    /// <summary>
    /// Controller-driven counterpart to <see cref="ImageSelectionWindow"/>.
    /// Same data shape (<see cref="ImageSelectionViewModel"/>), but rendered as
    /// a fullscreen overlay with focusable rows that toggle on the A button.
    /// Uses <see cref="XInputController"/> directly because BigBox does not
    /// forward gamepad input to plugin-launched windows.
    /// </summary>
    internal partial class BigBoxImagePickerWindow : Window
    {
        private readonly ImageSelectionViewModel _vm;
        private XInputController _gamepad;
        private bool _confirmed;

        public IReadOnlyList<LaunchBoxMetadataLookup.GameImage> SelectedImages =>
            _confirmed
                ? _vm.Options.Where(o => o.IsSelected).Select(o => o.Image).ToArray()
                : null;

        public BigBoxImagePickerWindow(string gameTitle,
                                       string preferredRegion,
                                       IReadOnlyList<LaunchBoxMetadataLookup.GameImage> images)
        {
            InitializeComponent();
            _vm = new ImageSelectionViewModel(gameTitle, preferredRegion, images);

            HeaderText.Text = _vm.Header;
            SubtitleText.Text = _vm.Subtitle;
            OptionsList.ItemsSource = _vm.Options;

            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _gamepad = new XInputController();
            _gamepad.ButtonPressed   += OnGamepadButton;
            _gamepad.DirectionPressed += OnGamepadDirection;
            _gamepad.Start();

            // Focus the first item so D-pad navigation works immediately.
            if (_vm.Options.Count > 0)
            {
                OptionsList.SelectedIndex = 0;
                OptionsList.UpdateLayout();
                var container = OptionsList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
                container?.Focus();
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try { _gamepad?.Dispose(); } catch { }
            _gamepad = null;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Mouse-and-keyboard fallback: Esc cancels, Enter confirms, Space
            // toggles. Lets a developer drive the picker without a controller.
            switch (e.Key)
            {
                case Key.Escape: Cancel(); e.Handled = true; break;
                case Key.Enter:  Confirm(); e.Handled = true; break;
                case Key.Space:  ToggleFocused(); e.Handled = true; break;
            }
        }

        private void OnGamepadButton(XInputController.Button btn)
        {
            switch (btn)
            {
                case XInputController.Button.A:     ToggleFocused();  break;
                case XInputController.Button.B:     Cancel();         break;
                case XInputController.Button.X:     SetAll(false);    break;
                case XInputController.Button.Y:     SetAll(true);     break;
                case XInputController.Button.Start: Confirm();        break;
            }
        }

        private void OnGamepadDirection(XInputController.Direction dir)
        {
            FocusNavigationDirection nav;
            switch (dir)
            {
                case XInputController.Direction.Up:    nav = FocusNavigationDirection.Up;    break;
                case XInputController.Direction.Down:  nav = FocusNavigationDirection.Down;  break;
                case XInputController.Direction.Left:  nav = FocusNavigationDirection.Left;  break;
                case XInputController.Direction.Right: nav = FocusNavigationDirection.Right; break;
                default: return;
            }
            if (Keyboard.FocusedElement is FrameworkElement fe)
                fe.MoveFocus(new TraversalRequest(nav));
        }

        private void ToggleFocused()
        {
            if (Keyboard.FocusedElement is ListBoxItem item &&
                item.DataContext is ImageOption opt)
            {
                opt.IsSelected = !opt.IsSelected;
            }
        }

        private void SetAll(bool value)
        {
            foreach (var o in _vm.Options) o.IsSelected = value;
        }

        private void Confirm()
        {
            _confirmed = true;
            Close();
        }

        private void Cancel()
        {
            _confirmed = false;
            Close();
        }
    }
}
