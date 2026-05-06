using System.Windows;

namespace Loadarr.UI
{
    internal partial class QueueWindow : Window
    {
        public QueueWindow()
        {
            InitializeComponent();
            DataContext = new QueueWindowViewModel();
        }
    }
}
