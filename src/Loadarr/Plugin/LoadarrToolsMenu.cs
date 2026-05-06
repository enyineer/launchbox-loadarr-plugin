using System.Drawing;
using Loadarr.UI;
using Unbroken.LaunchBox.Plugins;

namespace Loadarr.Plugin
{
    /// <summary>
    /// Adds "Loadarr — Find ROMs…" to the LaunchBox Tools menu and to the
    /// BigBox System menu. The two hosts open different windows: LaunchBox
    /// gets the regular mouse/keyboard SearchWindow, BigBox gets a fullscreen
    /// controller-friendly variant with an inline queue and on-screen keyboard.
    /// Host detection: PluginHelper.BigBoxMainViewModel is non-null in BigBox.
    /// </summary>
    public sealed class LoadarrToolsMenu : ISystemMenuItemPlugin
    {
        public string Caption => "Loadarr — Find ROMs…";
        public Image IconImage => null;
        public bool ShowInLaunchBox => true;
        public bool ShowInBigBox => true;
        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            if (PluginHelper.BigBoxMainViewModel != null)
            {
                var win = new BigBoxSearchWindow();
                win.Show();
                win.Activate();
            }
            else
            {
                var win = new SearchWindow();
                win.Show();
            }
        }
    }
}
