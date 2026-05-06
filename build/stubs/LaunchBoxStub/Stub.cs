using System;
using System.Drawing;

// Compile-time stub of the LaunchBox plugin API surface used by Loadarr.
// Lets the project build on macOS/Linux without LaunchBox installed.
// At runtime LaunchBox provides the real assembly.

namespace Unbroken.LaunchBox.Plugins
{
    public interface IGameMenuItemPlugin
    {
        string Caption { get; }
        Image IconImage { get; }
        bool ShowInLaunchBox { get; }
        bool ShowInBigBox { get; }
        bool SupportsMultipleGames { get; }
        bool GetIsValidForGame(Data.IGame selectedGame);
        bool GetIsValidForGames(Data.IGame[] selectedGames);
        void OnSelected(Data.IGame selectedGame);
        void OnSelected(Data.IGame[] selectedGames);
    }

    public interface ISystemMenuItemPlugin
    {
        string Caption { get; }
        Image IconImage { get; }
        bool ShowInLaunchBox { get; }
        bool ShowInBigBox { get; }
        bool AllowInBigBoxWhenLocked { get; }
        void OnSelected();
    }

    public static class PluginHelper
    {
        public static Data.IDataManager DataManager { get; set; }
        public static Data.ILaunchBoxMainViewModel LaunchBoxMainViewModel { get; set; }
        public static Data.IBigBoxMainViewModel BigBoxMainViewModel { get; set; }
    }
}

namespace Unbroken.LaunchBox.Plugins.Data
{
    public interface ILaunchBoxMainViewModel
    {
        void RefreshData();
    }

    // BigBox host. Null in regular LaunchBox; non-null in BigBox. There is no
    // RefreshData() — the documented refresh trick after AddNewGame is to call
    // ShowGame(newGame, FilterType.Platform) so BigBox re-binds its current view.
    public interface IBigBoxMainViewModel
    {
        void ShowGame(IGame game, FilterType filterType);
        void ShowPlatforms();
        void ShowOnScreenKeyboard();
    }

    public enum FilterType
    {
        None = 0,
        Platform = 1,
        PlatformCategory = 2,
        Genre = 3,
        Series = 4,
        Publisher = 5,
        Developer = 6,
        Region = 7,
        Source = 8,
        Status = 9,
        ReleaseYear = 10,
        Playmode = 11,
        Rating = 12,
        Playlist = 13,
    }

    public interface IDataManager
    {
        IPlatform GetPlatformByName(string name);
        IPlatform AddNewPlatform(string name);
        IGame AddNewGame(string title);
        IEmulator[] GetAllEmulators();
        void Save(bool forceQuickReload);
    }

    public interface IPlatform
    {
        string Name { get; set; }
    }

    public interface IGame
    {
        string Title { get; set; }
        string Platform { get; set; }
        string ApplicationPath { get; set; }
        string EmulatorId { get; set; }
        string Region { get; set; }
        string Version { get; set; }
        string Notes { get; set; }
        string Source { get; set; }
        DateTime DateAdded { get; set; }

        // Populated by LaunchBoxMetadataLookup from <LaunchBox>\Metadata\metadata.xml.
        int? LaunchBoxDbId { get; set; }
        DateTime? ReleaseDate { get; set; }
        int? ReleaseYear { get; set; }
        string Developer { get; set; }
        string Publisher { get; set; }
        string GenresString { get; set; }
        float CommunityStarRating { get; set; }
        string WikipediaUrl { get; set; }
    }

    public interface IEmulator
    {
        string Id { get; }
        IEmulatorPlatform[] GetAllEmulatorPlatforms();
    }

    public interface IEmulatorPlatform
    {
        string Platform { get; }
    }
}
