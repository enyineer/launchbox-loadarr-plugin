using System.Drawing;
using Loadarr.UI;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace Loadarr.Plugin
{
    /// <summary>
    /// Adds a right-click entry on a game ("Search Loadarr for this title…")
    /// that opens the search window pre-filled with the game's title and platform.
    /// Useful for filling in a placeholder/missing ROM file on an existing entry.
    /// </summary>
    public sealed class LoadarrGameMenu : IGameMenuItemPlugin
    {
        public string Caption => "Search Loadarr for this title…";
        public Image IconImage => null;
        public bool ShowInLaunchBox => true;
        public bool ShowInBigBox => false;
        public bool SupportsMultipleGames => false;

        public bool GetIsValidForGame(IGame selectedGame) => selectedGame != null;
        public bool GetIsValidForGames(IGame[] selectedGames) => false;

        public void OnSelected(IGame selectedGame)
        {
            if (selectedGame == null) return;
            var win = new SearchWindow(selectedGame.Title, selectedGame.Platform);
            win.Show();
        }

        public void OnSelected(IGame[] selectedGames)
        {
            // multi-select unsupported
        }
    }
}
