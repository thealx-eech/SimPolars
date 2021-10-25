using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;
using Newtonsoft.Json.Linq;
using System.Windows.Media;
using System.Globalization;
using System.Threading;

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
        void Render(double airspeed_kph, bool stall_line = false);
        JObject getSettings();
        bool updateSetting(string setting_key, string setting_value);
    }
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Console.WriteLine("SimPolars MainWindow starting...");
            // have . as decimal point (German MS-Windows uses ,)
            CultureInfo culture = CultureInfo.CreateSpecificCulture("en-US");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

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
                forceHorizontalFlight.IsChecked = (string)settings.GetValue("forceHorizontalFlight") == "true";
                HidePoints.IsChecked = (string)settings.GetValue("hidePoints") == "true";

                if (int.TryParse((string)settings.GetValue("speed_measurement"), out int speed_measurement))
                {
                    speedMeasurement.SelectedIndex = speed_measurement;
                }

                setComboboxValue(measurementPrecision, (string)settings.GetValue("precision"));
                stallBreakpoint.Text = (string)settings.GetValue("stallBreakpoint");
                setComboboxValue(curveResolution, (string)settings.GetValue("curveResolution"));


                graphBgImagePath.TextChanged += new TextChangedEventHandler(graphBgImagePath_changed);

                oBaseSimConnectWrapper.SetWindowHandle(GetHWinSource().Handle);

                oBaseSimConnectWrapper.AddFlightDataRequest();

                oBaseSimConnectWrapper.Render(0);

            }
        }

        void setComboboxValue(ComboBox cb, string value)
        {
            int index = 0;
            foreach (ComboBoxItem item in cb.Items)
            {
                if (item.Tag != null && item.Tag.ToString() == value)
                {
                    cb.SelectedIndex = index;
                    break;
                }

                index++;
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

        private void speedMeasurement_changed(object Sender, SelectionChangedEventArgs e)
        {
            Console.WriteLine("speedMeasurement changed " + speedMeasurement.SelectedIndex.ToString());
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("speed_measurement", speedMeasurement.SelectedIndex.ToString());
            }
        }

        private void precision_changed(object Sender, SelectionChangedEventArgs e)
        {
            Console.WriteLine("precision changed " + ((ComboBoxItem)measurementPrecision.SelectedItem).Tag.ToString());
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("precision", ((ComboBoxItem)measurementPrecision.SelectedItem).Tag.ToString());
            }
        }

        private void stallBreakpoint_LostFocus(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("stallBreakpoint changed " + stallBreakpoint.Text);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("stallBreakpoint", stallBreakpoint.Text);
            }
        }

        private void curveResolution_changed(object Sender, SelectionChangedEventArgs e)
        {
            Console.WriteLine("curveResolution changed " + curveResolution.SelectedIndex.ToString());
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("curveResolution", ((ComboBoxItem)curveResolution.SelectedItem).Tag.ToString());
            }
        }


        private void horizontal_flight_changed(object Sender, EventArgs e)
        {
            Console.WriteLine("horizontal flight changed " + forceHorizontalFlight.IsChecked);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("forceHorizontalFlight", forceHorizontalFlight.IsChecked == true ? "true" : "false");
            }
        }

        private void hide_points_changed(object Sender, EventArgs e)
        {
            Console.WriteLine("hide points changed " + HidePoints.IsChecked);
            if (this.DataContext is IBaseSimConnectWrapper oBaseSimConnectWrapper)
            {
                oBaseSimConnectWrapper.updateSetting("hidePoints", HidePoints.IsChecked == true ? "true" : "false");
                oBaseSimConnectWrapper.Render(0, true);
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

        public void scaleGrid(object sender, RoutedEventArgs e)
        {
            double value = ((Slider)sender).Value;
            if (captureCanvas != null)
                captureCanvas.LayoutTransform = new ScaleTransform(value, value);
        }
    } // end class MainWindow
}
