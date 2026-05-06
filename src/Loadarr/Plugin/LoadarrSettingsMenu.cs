using System.Drawing;
using Loadarr.UI;
using Unbroken.LaunchBox.Plugins;

namespace Loadarr.Plugin
{
    /// <summary>
    /// Adds "Loadarr — Settings…" to the LaunchBox Tools menu. Hidden in
    /// BigBox: the JSON file under %APPDATA%\Loadarr\ is the escape hatch
    /// for that audience (a controller-driven folder picker isn't worth
    /// the cost for the handful of fields involved).
    /// </summary>
    public sealed class LoadarrSettingsMenu : ISystemMenuItemPlugin
    {
        public string Caption => "Loadarr — Settings…";
        public Image IconImage => null;
        public bool ShowInLaunchBox => true;
        public bool ShowInBigBox => false;
        public bool AllowInBigBoxWhenLocked => false;

        public void OnSelected()
        {
            var win = new SettingsWindow();
            win.Show();
        }
    }
}
