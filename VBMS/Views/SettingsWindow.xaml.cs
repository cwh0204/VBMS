using System.Windows;
using VBMS.ViewModels;

namespace VBMS.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is SettingsViewModel vm)
                {
                    vm.RequestClose += Close;
                }
            };
        }
    }
}