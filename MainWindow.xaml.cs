using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Navigation;

namespace Simvars
{
    interface IBaseSimConnectWrapper
    {
        int GetUserSimConnectWinEvent();
        void ReceiveSimConnectMessage();
        void SetWindowHandle(IntPtr _hWnd);
        void LoadSettings(string _sFileName);
        void Disconnect();
        void AddFlightDataRequest();
        void ToggleRender(double airspeed_kph);
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
                //***********************************************************************
                // Load settings from settings.json if available, otherwise use defaults
                //***********************************************************************
                string settings_file = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\settings.json";
                if (File.Exists(settings_file))
                {
                    oBaseSimConnectWrapper.LoadSettings(settings_file);
                    graphBgImagePath.Text = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\polar.png";
                    oBaseSimConnectWrapper.ToggleRender(0);
                }

                oBaseSimConnectWrapper.SetWindowHandle(GetHWinSource().Handle);

                oBaseSimConnectWrapper.AddFlightDataRequest();

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
