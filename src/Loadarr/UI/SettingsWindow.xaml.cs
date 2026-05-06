using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Loadarr.Settings;

namespace Loadarr.UI
{
    internal partial class SettingsWindow : Window
    {
        private readonly LoadarrSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = LoadarrSettings.Load();

            DownloadDirBox.Text = _settings.EffectiveDownloadDirectory;
            ExtractCheck.IsChecked = _settings.ExtractDownloadedArchives;
            DebugCheck.IsChecked = _settings.EnableDebugLogging;
            TimeoutBox.Text = _settings.SearchTimeoutSeconds.ToString();
        }

        private void OnBrowseDownloadDir(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose where Loadarr should save downloaded ROMs.";
                if (Directory.Exists(DownloadDirBox.Text))
                    dlg.SelectedPath = DownloadDirBox.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    DownloadDirBox.Text = dlg.SelectedPath;
            }
        }

        private void OnOpenLogFolder(object sender, RoutedEventArgs e)
        {
            // Resolve the log directory the same way Log.cs does, so this
            // button stays correct if EnableDebugLogging is toggled mid-session
            // and the log file hasn't been created yet.
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Loadarr");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't open the log folder: " + ex.Message,
                    "Loadarr", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static readonly Regex IntOnly = new Regex(@"^[0-9]+$");
        private void OnTimeoutPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Block non-digit characters so the field can't end up holding
            // garbage that fails to parse on save.
            e.Handled = !IntOnly.IsMatch(e.Text);
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            var dir = (DownloadDirBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(dir))
            {
                MessageBox.Show(this, "Download directory can't be empty.",
                    "Loadarr", MessageBoxButton.OK, MessageBoxImage.Warning);
                DownloadDirBox.Focus();
                return;
            }

            if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout < 1)
            {
                MessageBox.Show(this, "Search timeout must be a positive number of seconds.",
                    "Loadarr", MessageBoxButton.OK, MessageBoxImage.Warning);
                TimeoutBox.Focus();
                return;
            }

            // Store empty string when the user picked the default, so the JSON
            // doesn't pin them to a path that was only correct on day one.
            _settings.DownloadDirectory =
                string.Equals(dir, _settings.DefaultDownloadDirectory, StringComparison.OrdinalIgnoreCase)
                    ? null : dir;
            _settings.ExtractDownloadedArchives = ExtractCheck.IsChecked == true;
            _settings.EnableDebugLogging = DebugCheck.IsChecked == true;
            _settings.SearchTimeoutSeconds = timeout;

            try
            {
                _settings.Save();
                Loadarr.Services.Log.Enabled = _settings.EnableDebugLogging;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save settings: " + ex.Message,
                    "Loadarr", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Close();
    }
}
