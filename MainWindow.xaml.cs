using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Source
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            rtc = new WebRtc(ice =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    txt_myIce.Text = ice;
                });
            });
        }
        WebRtc rtc;
        bool IsOffer = false;
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            rtc.SetMonitor();
            txt_MyDes.Text = rtc.createOffer();
            IsOffer = true;
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            string des = txt_Des.Text.Trim();
            if (string.IsNullOrEmpty(des)) return;

            if (!IsOffer)
            {
                rtc.GetMonitor(this, img);
                txt_MyDes.Text = rtc.createAnswer(des);
            }
            else
            {
                rtc.SetAnswer(des);
            }
        }

        private void Button_Ice(object sender, RoutedEventArgs e)
        {
            string ice = txt_Ice.Text.Trim();
            if (string.IsNullOrEmpty(ice)) return;
            rtc.SetIce(ice);
        }
    }
}
