using System.Windows;

namespace Loadarr.UI
{
    public partial class SearchWindow : Window
    {
        public SearchWindow()
        {
            InitializeComponent();
        }

        public SearchWindow(string prefilledQuery, string prefilledPlatform) : this()
        {
            if (DataContext is SearchWindowViewModel vm)
            {
                vm.Query = prefilledQuery;
                vm.PlatformHint = prefilledPlatform;
            }
        }
    }
}
