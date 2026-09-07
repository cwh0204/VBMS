using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VBMS.ViewModels;

namespace VBMS.Views
{
    public partial class RackDetailWindow : Window
    {
        // 셀 가로 너비(52px) + 좌우 여백(Margin 1+1 = 2px) = 연당 54.0px 스냅
        private const double BayCellWidth = 54.0;

        public RackDetailWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 01~25연, 26~50연, 51~75연 빠른 구역 이동 클릭 핸들러
        /// </summary>
        private void JumpBay_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int targetBay))
            {
                if (MainCellScrollViewer == null) return;

                double targetOffset = Math.Max(0, (targetBay - 1) * BayCellWidth);
                MainCellScrollViewer.ScrollToHorizontalOffset(targetOffset);
            }
        }

        /// <summary>
        /// 마우스 우클릭 시 해당 셀을 즉각 선택하여 ListBox 단일 ContextMenu와 바인딩 연동
        /// </summary>
        private void CellListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && e.OriginalSource is DependencyObject dep)
            {
                var item = ItemsControl.ContainerFromElement(listBox, dep) as ListBoxItem;
                if (item != null)
                {
                    item.IsSelected = true;
                    item.Focus();
                }
            }
        }

        /// <summary>
        /// 키보드 방향키 탐색 및 ScaleY="-1" 상하 반전 이동 제어
        /// </summary>
        private void CellListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem != null)
            {
                int currentIndex = listBox.SelectedIndex;
                int totalItems = listBox.Items.Count;
                if (totalItems == 0) return;

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
                        if (currentIndex + columns < totalItems)
                            newIndex = currentIndex + columns;
                        e.Handled = true;
                        break;

                    case Key.Down:
                        if (currentIndex - columns >= 0)
                            newIndex = currentIndex - columns;
                        e.Handled = true;
                        break;
                }

                if (newIndex != currentIndex)
                {
                    listBox.SelectedIndex = newIndex;

                    listBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (listBox.ItemContainerGenerator.ContainerFromIndex(newIndex) is ListBoxItem item)
                        {
                            item.Focus();
                            item.BringIntoView();
                        }
                    }));
                }
            }
        }
    }
}