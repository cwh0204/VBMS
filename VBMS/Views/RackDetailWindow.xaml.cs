using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VBMS.ViewModels;

namespace VBMS.Views
{
    public partial class RackDetailWindow : Window
    {
        public RackDetailWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// UniformGrid 환경에서 방향키 탐색 및 ScaleY="-1" 상하 반전 제어
        /// </summary>
        private void CellListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem != null)
            {
                int currentIndex = listBox.SelectedIndex;
                int totalItems = listBox.Items.Count;
                if (totalItems == 0) return;

                // ViewModel에서 컬럼 수(GridColumns) 가져오기
                int columns = 70;
                if (DataContext is RackDetailViewModel vm && vm.Rack != null)
                {
                    columns = vm.Rack.GridColumns;
                }

                int newIndex = currentIndex;

                switch (e.Key)
                {
                    case Key.Left:
                        newIndex = Math.Max(0, currentIndex - 1);
                        e.Handled = true;
                        break;

                    case Key.Right:
                        newIndex = Math.Min(totalItems - 1, currentIndex + 1);
                        e.Handled = true;
                        break;

                    case Key.Up:
                        // ScaleY="-1" 반전 상태이므로 화면 상단 이동은 Index + columns (상위 단)
                        if (currentIndex + columns < totalItems)
                            newIndex = currentIndex + columns;
                        e.Handled = true;
                        break;

                    case Key.Down:
                        // ScaleY="-1" 반전 상태이므로 화면 하단 이동은 Index - columns (하위 단)
                        if (currentIndex - columns >= 0)
                            newIndex = currentIndex - columns;
                        e.Handled = true;
                        break;
                }

                if (newIndex != currentIndex)
                {
                    listBox.SelectedIndex = newIndex;

                    // 이동된 셀 요소로 포커스 지정 (키보드 연동 탐색 활성화)
                    listBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (listBox.ItemContainerGenerator.ContainerFromIndex(newIndex) is ListBoxItem item)
                        {
                            item.Focus();
                        }
                    }));
                }
            }
        }
    }
}