using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Loadarr.Services;
using Loadarr.Sources;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace Loadarr.UI
{
    /// <summary>
    /// Controller-driven BigBox overlay. Reuses <see cref="SearchWindowViewModel"/>
    /// for the search/download/queue services, but replaces the mouse-driven
    /// chrome (TextBox typing, ImageSelectionWindow modal) with BigBox-friendly
    /// alternatives: an in-window virtual keyboard for query entry, an inline
    /// queue panel, and an auto-pick image step. Gamepad input is consumed via
    /// <see cref="XInputController"/> since BigBox does not forward controller
    /// input to plugin-launched windows.
    /// </summary>
    internal partial class BigBoxSearchWindow : Window
    {
        private XInputController _gamepad;
        private SearchWindowViewModel Vm => (SearchWindowViewModel)DataContext;

        // True if any imports finished successfully while the window was open.
        // Drives the BigBox refresh nudge on close.
        private bool _anyImportsCompleted;

        public BigBoxSearchWindow()
        {
            InitializeComponent();
            BuildKeyboard();

            // Use the controller-friendly picker instead of the mouse-driven
            // dialog. Pre-check defaults match the desktop dialog so users get
            // the same starting set with one less navigation step.
            // We pause our own gamepad polling for the duration of the modal
            // so button presses don't double-fire into both windows.
            Vm.ImagePicker = (result, available) =>
            {
                _gamepad?.Stop();
                try
                {
                    var picker = new BigBoxImagePickerWindow(result.Title, result.Region, available)
                    {
                        Owner = this,
                    };
                    picker.ShowDialog();
                    return picker.SelectedImages; // null = cancelled
                }
                finally
                {
                    _gamepad?.Start();
                }
            };

            // Show queue inline. Bound in code-behind so we don't need a global
            // ItemsSource binding to a static singleton.
            QueueList.ItemsSource = DownloadQueueService.Instance.Jobs;
            DownloadQueueService.Instance.Jobs.CollectionChanged += (_, __) => UpdateQueueEmptyState();
            UpdateQueueEmptyState();

            // Track import completions so we can decide whether to nudge BigBox
            // to refresh on close.
            foreach (var job in DownloadQueueService.Instance.Jobs) job.PropertyChanged += OnJobPropertyChanged;
            DownloadQueueService.Instance.Jobs.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (DownloadJob j in e.NewItems) j.PropertyChanged += OnJobPropertyChanged;
            };

            // Auto-hide the keyboard once a search produces results so the
            // results list isn't squashed under it. Pressing X brings it back.
            Vm.PropertyChanged += OnVmPropertyChanged;

            Loaded += OnLoaded;
            Closed += OnClosed;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private bool _wasBusy;
        private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SearchWindowViewModel.IsBusy)) return;
            // Detect the busy → idle transition (a search just ended). If the
            // keyboard is still up and we have results, collapse and focus the
            // first row. Empty results: keep the keyboard up so the user can
            // refine the query without an extra button press.
            if (_wasBusy && !Vm.IsBusy && KeyboardPanel.Visibility == Visibility.Visible
                && Vm.Results.Count > 0)
            {
                SetKeyboardVisible(false);
            }
            _wasBusy = Vm.IsBusy;
        }

        private void SetKeyboardVisible(bool visible)
        {
            KeyboardPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
                // Focus the query field so the active input is highlighted;
                // user D-pads down into the keyboard to type.
                QueryField.Focus();
            }
            else
            {
                // Prefer landing on the first result; fall back to the query
                // field so the user always has a focused element to navigate
                // from (e.g. when HIDE is pressed before any search has run).
                if (Vm.Results.Count > 0) FocusFirstResult();
                else QueryField.Focus();
            }
        }

        private void OnQueryFieldClick(object sender, RoutedEventArgs e)
        {
            // Either the user pressed A on the focused query field, or clicked
            // it with the mouse. Either way bring the keyboard back so they
            // can edit the query.
            SetKeyboardVisible(true);
        }

        private void FocusFirstResult()
        {
            if (Vm.Results.Count == 0) return;
            ResultsList.SelectedIndex = 0;
            ResultsList.UpdateLayout();
            var container = ResultsList.ItemContainerGenerator
                .ContainerFromIndex(0) as ListBoxItem;
            container?.Focus();
        }

        private void OnJobPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DownloadJob.Status)) return;
            if (sender is DownloadJob j && j.Status == JobStatus.Done) _anyImportsCompleted = true;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Start gamepad polling once the window is fully realized so focus
            // moves don't fire against unmaterialized children.
            _gamepad = new XInputController();
            _gamepad.ButtonPressed   += OnGamepadButton;
            _gamepad.DirectionPressed += OnGamepadDirection;
            _gamepad.Start();

            // Default focus: the query field. The keyboard is already visible
            // (initial state); user D-pads down into it to type, or presses X
            // later to hide/show it.
            QueryField.Focus();
        }

        private void OnClosed(object sender, EventArgs e)
        {
            try { _gamepad?.Dispose(); } catch { }
            _gamepad = null;

            // Nudge BigBox to re-bind its current view so newly imported games
            // appear without a restart. ShowPlatforms() returns the user to the
            // platform list, which is the natural "what did I just add" landing
            // spot. Skipped when nothing was imported during this session.
            if (_anyImportsCompleted)
            {
                try { PluginHelper.BigBoxMainViewModel?.ShowPlatforms(); }
                catch (Exception ex) { Log.Warn("BigBox refresh failed: " + ex.Message); }
            }
        }

        // ---------- Keyboard input (physical) ----------

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        // ---------- Gamepad input ----------

        private void OnGamepadButton(XInputController.Button btn)
        {
            switch (btn)
            {
                case XInputController.Button.A:
                    InvokeFocused();
                    break;
                case XInputController.Button.B:
                    Close();
                    break;
                case XInputController.Button.X:
                    SetKeyboardVisible(KeyboardPanel.Visibility != Visibility.Visible);
                    break;
                case XInputController.Button.Start:
                    Close();
                    break;
                case XInputController.Button.LeftShoulder:
                    MoveFocusDir(FocusNavigationDirection.Previous);
                    break;
                case XInputController.Button.RightShoulder:
                    MoveFocusDir(FocusNavigationDirection.Next);
                    break;
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
            MoveFocusDir(nav);
        }

        private void MoveFocusDir(FocusNavigationDirection dir)
        {
            if (Keyboard.FocusedElement is FrameworkElement fe)
                fe.MoveFocus(new TraversalRequest(dir));
            else if (KeyboardKeys.Children.Count > 0)
                ((UIElement)KeyboardKeys.Children[0]).Focus();
        }

        private void InvokeFocused()
        {
            var focused = Keyboard.FocusedElement;
            switch (focused)
            {
                case Button b:
                    if (b.Command?.CanExecute(b.CommandParameter) == true)
                        b.Command.Execute(b.CommandParameter);
                    else
                        b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    break;
                case ListBoxItem item:
                    // A on a result row = "queue this one". Set selection so
                    // SelectedResult is up-to-date before the command reads it,
                    // then dispatch the download flow (image picker → queue).
                    item.IsSelected = true;
                    if (ItemsControl.ItemsControlFromItemContainer(item) == ResultsList)
                        TriggerDownload();
                    break;
                default:
                    // Some focused element without a primary action — no-op.
                    break;
            }
        }

        private void TriggerDownload()
        {
            if (Vm.DownloadCommand?.CanExecute(null) == true)
                Vm.DownloadCommand.Execute(null);
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Desktop fallback so the BigBox window is testable with a mouse.
            // Only fire when an actual row was clicked (not the empty area).
            if (e.OriginalSource is DependencyObject src &&
                FindAncestor<ListBoxItem>(src) != null)
            {
                TriggerDownload();
            }
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        // ---------- Virtual keyboard ----------

        private void BuildKeyboard()
        {
            // Letters + digits row layout. 4 rows × 10 cols = 40 cells; we
            // populate 36 (letters + digits) and pad the last 4 with blank
            // placeholders so the grid stays rectangular.
            string[] rows =
            {
                "1234567890",
                "QWERTYUIOP",
                "ASDFGHJKL'",
                "ZXCVBNM,.-",
            };

            foreach (var row in rows)
            {
                foreach (var ch in row)
                {
                    var key = MakeKey(ch.ToString(), () => AppendQuery(ch.ToString()));
                    KeyboardKeys.Children.Add(key);
                }
            }

            // Special row: ␣ SPACE / ⌫ BACK / ✕ CLEAR / ⌄ HIDE / SEARCH.
            // Icons + label for unambiguous recognition; SEARCH stays text-only
            // because it's the primary action and benefits from full readability.
            // HIDE is a manual escape-hatch for when the user wants to view the
            // queue (or just see more results) without searching first.
            KeyboardSpecial.Children.Add(MakeKey("␣",  () => AppendQuery(" "),          wide: true)); // OPEN BOX
            KeyboardSpecial.Children.Add(MakeKey("⌫",  Backspace,                       wide: true)); // ERASE TO THE LEFT
            KeyboardSpecial.Children.Add(MakeKey("✕",  ClearQuery,                      wide: true)); // MULTIPLICATION X
            KeyboardSpecial.Children.Add(MakeKey("⌄",  () => SetKeyboardVisible(false), wide: true)); // DOWN ARROWHEAD
            KeyboardSpecial.Children.Add(MakeKey("SEARCH",  InvokeSearch,                    wide: true, accent: true, name: "SearchKey"));
        }

        private Button MakeKey(string label, Action action, bool wide = false, bool accent = false, string name = null)
        {
            var styleKey = accent ? "AccentKeyButton" : (wide ? "WideKeyButton" : "KeyButton");
            var btn = new Button
            {
                Content = label,
                Style = (Style)FindResource(styleKey),
            };
            if (!string.IsNullOrEmpty(name)) RegisterName(name, btn);
            btn.Click += (_, __) => action();
            return btn;
        }

        private void AppendQuery(string s)
        {
            Vm.Query = (Vm.Query ?? string.Empty) + s;
        }

        private void Backspace()
        {
            var q = Vm.Query ?? string.Empty;
            if (q.Length == 0) return;
            Vm.Query = q.Substring(0, q.Length - 1);
        }

        private void ClearQuery()
        {
            Vm.Query = string.Empty;
        }

        private void InvokeSearch()
        {
            Vm.SearchCommand?.Execute(null);
        }

        private void UpdateQueueEmptyState()
        {
            var hasJobs = DownloadQueueService.Instance.Jobs.Count > 0;
            QueueEmptyHint.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
