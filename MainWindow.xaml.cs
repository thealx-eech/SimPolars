using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using Newtonsoft.Json.Linq;
using System.Windows.Media;

namespace Simvars
{
    interface IBaseSimConnectWrapper
    {
        string getBaseDirectory();

        int GetUserSimConnectWinEvent();
        void ReceiveSimConnectMessage();
        void SetWindowHandle(IntPtr _hWnd);
        void Disconnect();
        void AddFlightDataRequest();
        void ToggleRender(double airspeed_kph);
        JObject getSettings();
        bool updateSetting(string setting_key, string setting_value);
    }
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Console.WriteLine("SimPolars MainWindow starting...");

            this.DataContext = new SimvarsViewModel(this);

            InitializeComponent();
        }

        protected HwndSource GetHWinSource()
        {
            return PresentationSource.FromVisual(this) as HwndSource;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            GetHWinSource().AddHook(WndProc);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                //graphBgImagePath.Text = oBaseSimConnectWrapper.getBaseDirectory() + "\\polar.png";

                JObject settings = oBaseSimConnectWrapper.getSettings();

                // Initialize app textboxes with settings values
                graphXstart.Text = (string)settings.GetValue("airspeed_min_kph");
                graphXend.Text = (string)settings.GetValue("airspeed_max_kph");
                graphYstart.Text = (string)settings.GetValue("sink_min_ms");
                graphYend.Text = (string)settings.GetValue("sink_max_ms");
                graphBgImagePath.Text = (string)settings.GetValue("polar_image");

                graphBgImagePath.TextChanged += new TextChangedEventHandler(graphBgImagePath_changed);

                oBaseSimConnectWrapper.SetWindowHandle(GetHWinSource().Handle);

                oBaseSimConnectWrapper.AddFlightDataRequest();

                oBaseSimConnectWrapper.ToggleRender(0);

            }
        }


        private void graphXstart_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = graphXstart.Text;
            Console.WriteLine("graphXstart LostFocus " + text);
            double n;
            if (double.TryParse(text, out n))
            {
                if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
                {
                    oBaseSimConnectWrapper.updateSetting("airspeed_min_kph", graphXstart.Text);
                }
            }
        }
        private void graphXend_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = graphXend.Text;
            Console.WriteLine("graphXend LostFocus " + text);
            double n;
            if (double.TryParse(text, out n))
            {
                if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
                {
                    oBaseSimConnectWrapper.updateSetting("airspeed_max_kph", graphXend.Text);
                }
            }
        }
        private void graphYstart_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = graphYstart.Text;
            Console.WriteLine("graphYstart LostFocus " + text);
            double n;
            if (double.TryParse(text, out n))
            {
                if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
                {
                    oBaseSimConnectWrapper.updateSetting("sink_min_ms", graphYstart.Text);
                }
            }
        }
        private void graphYend_LostFocus(object sender, RoutedEventArgs e)
        {
            string text = graphYend.Text;
            Console.WriteLine("graphXstart LostFocus " + text);
            double n;
            if (double.TryParse(text, out n))
            {
                if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
                {
                    oBaseSimConnectWrapper.updateSetting("sink_max_ms", graphYend.Text);
                }
            }
        }

        private void textChanged_double(object sender, TextChangedEventArgs e)
        {
            TextBox t = e.Source as TextBox;
            string text = t.Text;
            if (text.Length == 0)
            {
                t.Background = Brushes.White;
            }
            else
            {
                double n;
                if (double.TryParse(text, out n))
                {
                    t.Background = Brushes.White;
                }
                else
                {
                    t.Background = Brushes.Red;
                }
            }
        }
        private void graphBgImagePath_changed(object Sender, TextChangedEventArgs e)
        {
            Console.WriteLine("graphBgImagePath changed " + graphBgImagePath.Text);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("polar_image", graphBgImagePath.Text);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, int iMsg, IntPtr hWParam, IntPtr hLParam, ref bool bHandled)
        {
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                try
                {
                    if (iMsg == oBaseSimConnectWrapper.GetUserSimConnectWinEvent())
                    {
                        //Console.WriteLine("SimConnectWinEvent received...");
                        oBaseSimConnectWrapper.ReceiveSimConnectMessage();
                    }
                }
                catch
                {
                    oBaseSimConnectWrapper.Disconnect();
                }
            }

            return IntPtr.Zero;
        }

        private void LinkOnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(e.Uri.ToString());
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            string sText = e.Text;
            foreach (char c in sText)
            {
                if ( ! (('0' <= c && c <= '9') || c == '+' || c == '-' || c == ',') )
                {
                    e.Handled = true;
                    break;
                }
            }
        }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is Slider oSlider && this.DataContext is SimvarsViewModel oContext)
            {
                oContext.SetTickSliderValue((int)oSlider.Value);
            }
        }

        private void Tick_Mode(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && this.DataContext is SimvarsViewModel oContext)
            {
                oContext.SetTickMode(chk.IsChecked);
            }

        }

        private void imgLoadEvent(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            if (openFileDialog.ShowDialog() == true)
                graphBgImagePath.Text = openFileDialog.FileName;
        }
    } // end class MainWindow
}
