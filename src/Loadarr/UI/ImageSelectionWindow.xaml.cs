using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Loadarr.Services;

namespace Loadarr.UI
{
    internal partial class ImageSelectionWindow : Window
    {
        private readonly ImageSelectionViewModel _vm;

        public ImageSelectionWindow(string gameTitle,
                                    string preferredRegion,
                                    IReadOnlyList<LaunchBoxMetadataLookup.GameImage> available)
        {
            InitializeComponent();
            _vm = new ImageSelectionViewModel(gameTitle, preferredRegion, available);
            DataContext = _vm;
        }

        public IReadOnlyList<LaunchBoxMetadataLookup.GameImage> SelectedImages =>
            _vm.Options.Where(o => o.IsSelected).Select(o => o.Image).ToList();

        private void OnOkClicked(object sender, RoutedEventArgs e) { DialogResult = true; }
        private void OnCancelClicked(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
