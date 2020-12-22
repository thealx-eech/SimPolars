using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using Microsoft.FlightSimulator.SimConnect;
using Newtonsoft.Json;

namespace Simvars
{
    public enum DEFINITION
    {
        Dummy = 0
    };

    public enum REQUEST
    {
        Dummy = 0
    };

    public class SetValueItem
    {
        public SetValueItem(Enum eDef, uint sIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG dEFAULT, object dValue)
        {
            DefineID = eDef;
            ObjectID = sIMCONNECT_OBJECT_ID_USER;
            Flags = dEFAULT;
            pDataSet = dValue;
        }

        public Enum DefineID { get; set; }
        public uint ObjectID { get; set; }
        public SIMCONNECT_DATA_SET_FLAG Flags { get; set; }
        public object pDataSet { get; set; }
    }

    public class SimvarRequest : ObservableObject
    {
        public DEFINITION eDef = DEFINITION.Dummy;
        public REQUEST eRequest = REQUEST.Dummy;

        public string sName { get; set; }

        public double dValue
        {
            get { return m_dValue; }
            set { this.SetProperty(ref m_dValue, value); }
        }
        private double m_dValue = 0.0;

        public string sUnits { get; set; }

        public bool bPending = true;
        public bool bStillPending
        {
            get { return m_bStillPending; }
            set { this.SetProperty(ref m_bStillPending, value); }
        }
        private bool m_bStillPending = false;
    };

    public class SimvarsViewModel : BaseViewModel, IBaseSimConnectWrapper
    {
        public static List<SetValueItem> SetValueItems = new List<SetValueItem>();

        #region IBaseSimConnectWrapper implementation

        /// User-defined win32 event
        public const int WM_USER_SIMCONNECT = 0x0402;

        /// Window handle
        private IntPtr m_hWnd = new IntPtr(0);

        /// SimConnect object
        private SimConnect m_oSimConnect = null;

        private bool captureActive = false;

        private Dictionary<string, double> prevVariables = new Dictionary<string, double>();
        private Dictionary<string, double> currVariables = new Dictionary<string, double>();

        private Dictionary<int, double>[] capturedDataArray = new Dictionary<int, double>[24];



        public bool bConnected
        {
            get { return m_bConnected; }
            private set { this.SetProperty(ref m_bConnected, value); }
        }
        private bool m_bConnected = false;

        private uint m_iCurrentDefinition = 0;
        private uint m_iCurrentRequest = 0;

        public int GetUserSimConnectWinEvent()
        {
            return WM_USER_SIMCONNECT;
        }

        public void ReceiveSimConnectMessage()
        {
            m_oSimConnect?.ReceiveMessage();
        }

        public void SetWindowHandle(IntPtr _hWnd)
        {
            m_hWnd = _hWnd;
        }

        public void Disconnect()
        {
            Console.WriteLine("Disconnect");

            m_oTimer.Stop();
            bOddTick = false;

            sw.Stop();

            if (m_oSimConnect != null)
            {
                /// Dispose serves the same purpose as SimConnect_Close()
                m_oSimConnect.Dispose();
                m_oSimConnect = null;
            }

            sConnectButtonLabel = "Connect";
            bConnected = false;

            // Set all requests as pending
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                oSimvarRequest.bPending = true;
                oSimvarRequest.bStillPending = true;
            }
        }

        public void CaptureStop()
        {
            Console.WriteLine("CaptureStop");

            sCaptureButtonLabel = "ENABLE DATA CAPTURE";

        }

        #endregion

        #region UI bindings

        public string sConnectButtonLabel
        {
            get { return m_sConnectButtonLabel; }
            private set { this.SetProperty(ref m_sConnectButtonLabel, value); }
        }
        private string m_sConnectButtonLabel = "Connect";

        public string sCaptureButtonLabel
        {
            get { return m_sCaptureButtonLabel; }
            private set { this.SetProperty(ref m_sCaptureButtonLabel, value); }
        }
        private string m_sCaptureButtonLabel = "ENABLE DATA CAPTURE";

        public bool bObjectIDSelectionEnabled
        {
            get { return m_bObjectIDSelectionEnabled; }
            set { this.SetProperty(ref m_bObjectIDSelectionEnabled, value); }
        }
        private bool m_bObjectIDSelectionEnabled = false;
        public SIMCONNECT_SIMOBJECT_TYPE eSimObjectType
        {
            get { return m_eSimObjectType; }
            set
            {
                this.SetProperty(ref m_eSimObjectType, value);
                bObjectIDSelectionEnabled = (m_eSimObjectType != SIMCONNECT_SIMOBJECT_TYPE.USER);
                ClearResquestsPendingState();
            }
        }
        private SIMCONNECT_SIMOBJECT_TYPE m_eSimObjectType = SIMCONNECT_SIMOBJECT_TYPE.USER;
        public ObservableCollection<uint> lObjectIDs { get; private set; }
        public uint iObjectIdRequest
        {
            get { return m_iObjectIdRequest; }
            set
            {
                this.SetProperty(ref m_iObjectIdRequest, value);
                ClearResquestsPendingState();
            }
        }
        private uint m_iObjectIdRequest = 0;
        

        public string[] aSimvarNames
        {
            get { return SimUtils.SimVars.Names; }
            private set { }
        }
        public string sSimvarRequest
        {
            get { return m_sSimvarRequest; }
            set { this.SetProperty(ref m_sSimvarRequest, value); }
        }
        private string m_sSimvarRequest = null;


        public string[] aUnitNames
        {
            get { return SimUtils.Units.Names; }
            private set { }
        }
        public string sUnitRequest
        {
            get { return m_sUnitRequest; }
            set { this.SetProperty(ref m_sUnitRequest, value); }
        }
        private string m_sUnitRequest = null;

        public string sSetValue
        {
            get { return m_sSetValue; }
            set { this.SetProperty(ref m_sSetValue, value); }
        }
        private string m_sSetValue = null;

        public ObservableCollection<SimvarRequest> lSimvarRequests { get; private set; }
        public SimvarRequest oSelectedSimvarRequest
        {
            get { return m_oSelectedSimvarRequest; }
            set { this.SetProperty(ref m_oSelectedSimvarRequest, value); }
        }
        private SimvarRequest m_oSelectedSimvarRequest = null;

        public uint[] aIndices
        {
            get { return m_aIndices; }
            private set { }
        }
        private readonly uint[] m_aIndices = new uint[100] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
                                                            10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
                                                            20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
                                                            30, 31, 32, 33, 34, 35, 36, 37, 38, 39,
                                                            40, 41, 42, 43, 44, 45, 46, 47, 48, 49,
                                                            50, 51, 52, 53, 54, 55, 56, 57, 58, 59,
                                                            60, 61, 62, 63, 64, 65, 66, 67, 68, 69,
                                                            70, 71, 72, 73, 74, 75, 76, 77, 78, 79,
                                                            80, 81, 82, 83, 84, 85, 86, 87, 88, 89,
                                                            90, 91, 92, 93, 94, 95, 96, 97, 98, 99 };
        public uint iIndexRequest
        {
            get { return m_iIndexRequest; }
            set { this.SetProperty(ref m_iIndexRequest, value); }
        }
        private uint m_iIndexRequest = 0;

        public bool bSaveValues
        {
            get { return m_bSaveValues; }
            set { this.SetProperty(ref m_bSaveValues, value); }
        }
        private bool m_bSaveValues = true;

        public bool bFSXcompatible
        {
            get { return m_bFSXcompatible; }
            set { this.SetProperty(ref m_bFSXcompatible, value); }
        }
        private bool m_bFSXcompatible = false;

        public bool bOddTick
        {
            get { return m_bOddTick; }
            set { this.SetProperty(ref m_bOddTick, value); }
        }
        private bool m_bOddTick = false;

        public ObservableCollection<string> lErrorMessages { get; private set; }


        public BaseCommand cmdToggleConnect { get; private set; }
        public BaseCommand cmdToggleCapture { get; private set; }
        public BaseCommand cmdToggleRender { get; private set; }
        public BaseCommand cmdToggleClear { get; private set; }
        public BaseCommand cmdToggleLoad { get; private set; }
        public BaseCommand cmdToggleSave { get; private set; }
        public BaseCommand cmdAddRequest { get; private set; }
        public BaseCommand cmdRemoveSelectedRequest { get; private set; }
        public BaseCommand cmdTrySetValue { get; private set; }
        public BaseCommand cmdSetValuePerm { get; private set; }
        public BaseCommand cmdLoadFiles { get; private set; }
        public BaseCommand cmdSaveFile { get; private set; }
        public BaseCommand cmdInsertThermal { get; private set; }

        public MainWindow parent;

        #endregion

        #region Real time

        private DispatcherTimer m_oTimer = new DispatcherTimer();
        private Stopwatch sw = new Stopwatch();
        private double swLast = 0;
        private double swElapsed;

        #endregion

        public SimvarsViewModel(MainWindow parent)
        {
            parent = (MainWindow)System.Windows.Application.Current.MainWindow;
            lObjectIDs = new ObservableCollection<uint>();
            lObjectIDs.Add(1);

            lSimvarRequests = new ObservableCollection<SimvarRequest>();
            lErrorMessages = new ObservableCollection<string>();

            cmdToggleConnect = new BaseCommand((p) => { ToggleConnect(); });
            cmdToggleCapture = new BaseCommand((p) => { ToggleCapture(); });
            cmdToggleRender = new BaseCommand((p) => { ToggleRender(); });
            cmdToggleClear = new BaseCommand((p) => { ToggleClear(); });
            cmdToggleLoad = new BaseCommand((p) => { ToggleLoad(); });
            cmdToggleSave = new BaseCommand((p) => { ToggleSave(); });
            cmdAddRequest = new BaseCommand((p) => { AddRequest(null, null); });
            cmdRemoveSelectedRequest = new BaseCommand((p) => { RemoveSelectedRequest(); });
            cmdTrySetValue = new BaseCommand((p) => { TrySetValue(); });
            cmdSetValuePerm = new BaseCommand((p) => { SetValuePerm(); });
            cmdLoadFiles = new BaseCommand((p) => { LoadFiles(); });
            cmdSaveFile = new BaseCommand((p) => { SaveFile(false); });
            cmdInsertThermal = new BaseCommand((p) => { InsertThermal(); });

            m_oTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
            m_oTimer.Tick += new EventHandler(OnTick);
        }

        private void Connect()
        {
            Console.WriteLine("Connect");

            try
            {
                /// The constructor is similar to SimConnect_Open in the native API
                m_oSimConnect = new SimConnect("Simconnect - Simvar test", m_hWnd, WM_USER_SIMCONNECT, null, bFSXcompatible? (uint)1 : 0);

                /// Listen to connect and quit msgs
                m_oSimConnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(SimConnect_OnRecvOpen);
                m_oSimConnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(SimConnect_OnRecvQuit);

                /// Listen to exceptions
                m_oSimConnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(SimConnect_OnRecvException);

                /// Catch a simobject data request
                m_oSimConnect.OnRecvSimobjectDataBytype += new SimConnect.RecvSimobjectDataBytypeEventHandler(SimConnect_OnRecvSimobjectDataBytype);

                sw.Start();
            }
            catch (COMException ex)
            {
                Console.WriteLine("Connection to KH failed: " + ex.Message);
            }
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("SimConnect_OnRecvOpen");
            Console.WriteLine("Connected to KH");

            sConnectButtonLabel = "Disconnect";
            bConnected = true;

            // Register pending requests
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                if (oSimvarRequest.bPending)
                {
                    oSimvarRequest.bPending = !RegisterToSimConnect(oSimvarRequest);
                    oSimvarRequest.bStillPending = oSimvarRequest.bPending;
                }
            }

            m_oTimer.Start();
            bOddTick = false;
        }

        /// The case where the user closes game
        private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("SimConnect_OnRecvQuit");
            Console.WriteLine("KH has exited");

            Disconnect();
        }

        private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            SIMCONNECT_EXCEPTION eException = (SIMCONNECT_EXCEPTION)data.dwException;
            Console.WriteLine("SimConnect_OnRecvException: " + eException.ToString());

            lErrorMessages.Add("SimConnect : " + eException.ToString());
        }

        private void SimConnect_OnRecvSimobjectDataBytype(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            //Console.WriteLine("SimConnect_OnRecvSimobjectDataBytype");

            uint iRequest = data.dwRequestID;
            uint iObject = data.dwObjectID;
            if (!lObjectIDs.Contains(iObject))
            {
                lObjectIDs.Add(iObject);
            }
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                if (iRequest == (uint)oSimvarRequest.eRequest && (!bObjectIDSelectionEnabled || iObject == m_iObjectIdRequest))
                {
                    double dValue = (double)data.dwData[0];
                    oSimvarRequest.dValue = dValue;
                    oSimvarRequest.bPending = false;
                    oSimvarRequest.bStillPending = false;

                    // STORE VALUE
                    currVariables[oSimvarRequest.sName] = dValue;
                }
            }
        }

        // May not be the best way to achive regular requests.
        // See SimConnect.RequestDataOnSimObject
        private void OnTick(object sender, EventArgs e)
        {
            bOddTick = !bOddTick;
            swElapsed = (sw.Elapsed.TotalMilliseconds - swLast) / 1000;
            swLast = sw.Elapsed.TotalMilliseconds;

            // READ DATA
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                if (!oSimvarRequest.bPending)
                {
                    m_oSimConnect?.RequestDataOnSimObjectType(oSimvarRequest.eRequest, oSimvarRequest.eDef, 0, m_eSimObjectType);
                    oSimvarRequest.bPending = true;
                }
                else
                {
                    oSimvarRequest.bStillPending = true;
                }
            }

            // WRITE DATE
            if (SetValueItems.Count > 0)
            {
                //Console.WriteLine("Updating " + SetValueItems.Count + " variables");
                foreach (SetValueItem itm in SetValueItems)
                {
                    m_oSimConnect.SetDataOnSimObject(itm.DefineID, itm.ObjectID, itm.Flags, itm.pDataSet);
                }
            }

            // AERODYNAMICS CAPTURE
            if (captureActive && prevVariables.Count > 0)
            {
                prevVariables.TryGetValue("AIRSPEED TRUE", out double airspeedOld);
                currVariables.TryGetValue("AIRSPEED TRUE", out double airspeed);
                prevVariables.TryGetValue("PLANE ALTITUDE", out double altitudeOld);
                currVariables.TryGetValue("PLANE ALTITUDE", out double altitude);
                currVariables.TryGetValue("FLAPS HANDLE INDEX", out double flaps);

                //Console.WriteLine(airspeedOld + " " + airspeed + " / " + altitudeOld + " " + altitude + " : " + swElapsed);

                if (airspeed != 0 && airspeedOld != 0 && altitude != 0 && altitudeOld != 0)
                {
                    //Console.Write("Capture " + (int)airspeed + " / " + sink);
                    if (capturedDataArray[(int)flaps] == null)
                        capturedDataArray[(int)flaps] = new Dictionary<int, double>();

                    double te = getTeValue(altitudeOld, altitude, airspeedOld, airspeed, swElapsed);

                    capturedDataArray[(int)flaps][(int)(airspeed)] = capturedDataArray[(int)flaps].ContainsKey((int)airspeed)
                        ? 0.9 * capturedDataArray[(int)flaps][(int)airspeed] + 0.1 * te : te;
                }

                ToggleRender();
            }

            // SAVE PREVIOUS STATE
            if (currVariables.Count > 0)
            {
                prevVariables = new Dictionary<string, double>(currVariables);
            }
        }

        //(h2 - h1) / t + (v2^2 - v1^2) / (2 * t * g)
        private double getTeValue(double altitudeOld, double altitude, double airspeedOld, double airspeed, double time)
        {
            return (altitude - altitudeOld) / time + (Math.Pow(airspeed, 2) - Math.Pow(airspeedOld, 2)) / (2 * time * 9.80665);
        }

        private SimvarRequest CreateSimvarRequest(string name, string unit)
        {
            return new SimvarRequest
            {
                eDef = (DEFINITION)m_iCurrentDefinition,
                eRequest = (REQUEST)m_iCurrentRequest,
                sName = name,
                sUnits = unit
            };
        }
        private void ToggleConnect()
        {
            if (m_oSimConnect == null)
            {
                try
                {
                    Connect();
                }
                catch (COMException ex)
                {
                    Console.WriteLine("Unable to connect to KH: " + ex.Message);
                }
            }
            else
            {
                Disconnect();
            }
        }
        private void ToggleCapture()
        {
            if (m_oSimConnect != null || captureActive == true)
            {
                if (captureActive == true) // DISABLE
                {
                    captureActive = false;
                    sCaptureButtonLabel = "ENABLE DATA CAPTURE";

                }
                else // ENABLE
                {
                    sSetValue = "0";
                    try { m_oSimConnect.SetDataOnSimObject(getSimvarId("PLANE PITCH DEGREES"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }
                    try { m_oSimConnect.SetDataOnSimObject(getSimvarId("PLANE BANK DEGREES"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }
                    try { m_oSimConnect.SetDataOnSimObject(getSimvarId("VELOCITY BODY Z"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)200); } catch (Exception e) { Console.WriteLine(e.Message); }
                    try { m_oSimConnect.SetDataOnSimObject(getSimvarId("FUEL TANK CENTER LEVEL:1"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }

                    captureActive = true;
                    sCaptureButtonLabel = "DISABLE DATA CAPTURE";
                }
            }
        }

        private DEFINITION getSimvarId(string name)
        {
            parent = (MainWindow)System.Windows.Application.Current.MainWindow;

            foreach (SimvarRequest request in lSimvarRequests)
            {
                Console.WriteLine("Comapre " + request.sName + " and " + name);
                if (request.sName == name)
                {
                    Console.WriteLine("Simvar " + request.sName + " ID: " + request.eDef);
                    return request.eDef;
                }
            }

            return (DEFINITION) (-1);
        }
        private void ToggleClear()
        {
            if (MessageBox.Show("You are goind to reset captured aerodynamics data", "Warning", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                capturedDataArray = new Dictionary<int, double>[24];
                parent = (MainWindow)System.Windows.Application.Current.MainWindow;
                parent.captureCanvas.Children.Clear();
                parent.captureLabels.Children.Clear();
            }
        }
        private void ToggleRender()
        {
            parent = (MainWindow)System.Windows.Application.Current.MainWindow;
            parent.captureCanvas.Children.Clear();
            parent.captureCanvas.Focus();
            parent.captureLabels.Children.Clear();

            // RENDER GRID
            if (double.TryParse((parent).graphXstart.Text, out double valXstart) &&
                double.TryParse((parent).graphXend.Text, out double valXend) &&
                double.TryParse((parent).graphYstart.Text, out double valYstart) &&
                double.TryParse((parent).graphYend.Text, out double valYend))
            {
                currVariables.TryGetValue("TOTAL WEIGHT", out double totalWeight);
                currVariables.TryGetValue("AIRSPEED TRUE", out double airspeedTrue);
                if (captureActive == true && airspeedTrue * 3.6 < valXstart) // DISABLE CAPTURE
                {
                    ToggleCapture();
                }

                double canvasWidth = parent.captureCanvas.Width;
                double canvasHeight = parent.captureCanvas.Height;
                parent.captureCanvas.Children.Add(getGraphLine(Colors.Black, 0, 0, 0, 1, canvasWidth, canvasHeight, 2));
                parent.captureCanvas.Children.Add(getGraphLine(Colors.Black, 0, 0, 1, 0, canvasWidth, canvasHeight, 2));

                double unitX = 1 / (valXend - valXstart);
                double unitY = 1 / (valYend - valYstart);

                // BACKGROUND IMAGE
                if (!string.IsNullOrEmpty(parent.graphBgImagePath.Text) && File.Exists(parent.graphBgImagePath.Text))
                {
                    Image img = new Image();
                    ImageBrush ib = new ImageBrush();
                    ib.Stretch = Stretch.Fill;
                    ib.ImageSource = new BitmapImage(new Uri(parent.graphBgImagePath.Text));
                    parent.captureCanvas.Background = ib;
                }

                // HORIZONTAL SPEED
                for (double k = Math.Ceiling(valXstart / 10) * 10 - Math.Ceiling(valXstart); k <= valXend - Math.Ceiling(valXstart); k += 10)
                {
                    parent.captureCanvas.Children.Add(getGraphLine(Colors.Gray, k * unitX, 0, k * unitX, 1, canvasWidth, canvasHeight, 1));
                    getCanvasTextLabel(parent.captureCanvas, Colors.Gray, k * unitX * canvasWidth, -16, (k + valXstart).ToString());
                }

                // VERTICAL SPEED
                for (double k = Math.Ceiling(valYstart) - Math.Ceiling(valYstart); k <= valYend - Math.Ceiling(valYstart); k += 1)
                {
                    parent.captureCanvas.Children.Add(getGraphLine(Colors.Gray, 0, k * unitY, 1, k * unitY, canvasWidth, canvasHeight, 1));
                    getCanvasTextLabel(parent.captureCanvas, Colors.Gray, -14, k * unitY * canvasHeight, (k + valYstart).ToString());
                }

                // CAPTURED VALUES
                int index = 1;
                foreach (var capturedData in capturedDataArray) {
                    if (capturedData != null && capturedData.Count > 0)
                    {
                        Color color = Color.FromRgb((byte)(255 - index % 4 * 127), (byte)(255 - (index+1) % 3 * 127), (byte)(255 - (index+2) % 3 * 127));
                        //Console.WriteLine(color.ToString());
                        parent.captureLabels.Children.Add(getTextLabel(color, "Flaps position #" + (index - 1) + " " + (int)totalWeight + "kg"));

                        foreach (var capturedValue in capturedData)
                        {
                            double airspeed = capturedValue.Key * 3.6;
                            double te = capturedValue.Value;
                            if (airspeed >= valXstart && airspeed <= valXend && (-te) >= valYstart && (-te) <= valYend)
                            {
                                parent.captureCanvas.Children.Add(getGraphLine(color, (airspeed + 0.01 - Math.Ceiling(valXstart)) * unitX, (-te + 0.02 - Math.Ceiling(valYstart)) * unitY,
                                    (airspeed - 0.01 - Math.Ceiling(valXstart)) * unitX, (-te - 0.02 - Math.Ceiling(valYstart)) * unitY, canvasWidth, canvasHeight, 3));
                                //Console.WriteLine(airspeed + " " + te + " / " + (airspeed - Math.Ceiling(valXstart)) * unitX + " " + (-te - Math.Ceiling(valYstart)) * unitY);
                            }
                        }
                    }
                    index++;
                }


            }
        }

        private void getCanvasTextLabel(Canvas cv, Color color, double x1, double y1, string text)
        {
            TextBlock label = new TextBlock();
            label.Foreground  = new SolidColorBrush(color);
            label.Text = text;
            cv.Children.Add(label);

            Canvas.SetLeft(label, x1);
            Canvas.SetTop(label, y1);
        }

        private TextBlock getTextLabel(Color color, string text)
        {
            TextBlock label = new TextBlock();
            label.Foreground = new SolidColorBrush(color);
            label.Text = text;

            return label;
        }

        private Line getGraphLine(Color color, double x1, double y1, double x2, double y2, double width, double height, int thickness = 1)
        {
            Line line = new Line();
            line.Stroke = new SolidColorBrush(color);
            line.StrokeThickness = thickness;
            line.X1 = x1 * width;
            line.Y1 = height - height * (1 - y1);
            line.X2 = x2 * width;
            line.Y2 = height - height * (1 - y2);

            return line;
        }

        private void ToggleLoad()
        {

        }

        private void ToggleSave()
        {
            int index = 0;
            foreach (var capturedData in capturedDataArray)
            {
                if (capturedData != null && capturedData.Count > 0)
                {
                    try
                    {
                        File.WriteAllText(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location) + "\\flaps"+index+".json", JsonConvert.SerializeObject(capturedData));
                    }
                    catch { }
                }

                index++;
            }
        }


        private void ClearResquestsPendingState()
        {
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                oSimvarRequest.bPending = false;
                oSimvarRequest.bStillPending = false;
            }
        }

        private bool RegisterToSimConnect(SimvarRequest _oSimvarRequest)
        {
            if (m_oSimConnect != null)
            {
                /// Define a data structure
                m_oSimConnect.AddToDataDefinition(_oSimvarRequest.eDef, _oSimvarRequest.sName, _oSimvarRequest.sUnits, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                /// IMPORTANT: Register it with the simconnect managed wrapper marshaller
                /// If you skip this step, you will only receive a uint in the .dwData field.
                m_oSimConnect.RegisterDataDefineStruct<double>(_oSimvarRequest.eDef);

                return true;
            }
            else
            {
                return false;
            }
        }

        private void AddRequest(string _sOverrideSimvarRequest, string _sOverrideUnitRequest, double value = 0)
        {
            Console.WriteLine("AddRequest " + _sOverrideSimvarRequest + " " + _sOverrideUnitRequest + " :" + value);

            string sNewSimvarRequest = _sOverrideSimvarRequest != null ? _sOverrideSimvarRequest : ((m_iIndexRequest == 0) ? m_sSimvarRequest : (m_sSimvarRequest + ":" + m_iIndexRequest));
            string sNewUnitRequest = _sOverrideUnitRequest != null ? _sOverrideUnitRequest : m_sUnitRequest;

            SimvarRequest oSimvarRequest = new SimvarRequest
            {
                eDef = (DEFINITION)m_iCurrentDefinition,
                eRequest = (REQUEST)m_iCurrentRequest,
                sName = sNewSimvarRequest,
                sUnits = sNewUnitRequest,
                dValue = value
            };

            oSimvarRequest.bPending = !RegisterToSimConnect(oSimvarRequest);
            oSimvarRequest.bStillPending = oSimvarRequest.bPending;

            lSimvarRequests.Add(oSimvarRequest);

            ++m_iCurrentDefinition;
            ++m_iCurrentRequest;
        }

        private void RemoveSelectedRequest()
        {
            lSimvarRequests.Remove(oSelectedSimvarRequest);
        }

        private void TrySetValue()
        {
            Console.WriteLine("TrySetValue: " + m_oSelectedSimvarRequest.eDef + " " + m_sSetValue);

            if (m_oSelectedSimvarRequest != null && m_sSetValue != null)
            {
                if (double.TryParse(m_sSetValue, NumberStyles.Any, null, out double dValue))
                {
                    m_oSimConnect.SetDataOnSimObject(m_oSelectedSimvarRequest.eDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, dValue);
                }
            }
        }
        private void SetValuePerm()
        {
            Console.WriteLine("SetValuePerm");
			if (m_oSelectedSimvarRequest != null && m_sSetValue != null)            {
                if (double.TryParse(m_sSetValue, NumberStyles.Any, null, out double dValue))
                {
                    SetValueItems.Add(new SetValueItem(m_oSelectedSimvarRequest.eDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, dValue));
                    //m_oSimConnect.SetDataOnSimObject(m_oSelectedSimvarRequest.eDef, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, dValue);
                }
            }

        }


        private void LoadFiles()
        {
            Microsoft.Win32.OpenFileDialog oOpenFileDialog = new Microsoft.Win32.OpenFileDialog();
            oOpenFileDialog.Multiselect = true;
            oOpenFileDialog.Filter = "Simvars files (*.simvars)|*.simvars";
            if (oOpenFileDialog.ShowDialog() == true)
            {
                foreach (string sFilename in oOpenFileDialog.FileNames)
                {
                    LoadFile(sFilename);
                }
            }
        }

        public void LoadFile(string _sFileName)
        {
            string[] aLines = System.IO.File.ReadAllLines(_sFileName);
            for (uint i = 0; i < aLines.Length; ++i)
            {
                // Format : Simvar,Unit
                string[] aSubStrings = aLines[i].Split(',');
                if (aSubStrings.Length >= 2) // format check
                {
                    // values check
                    string[] aSimvarSubStrings = aSubStrings[0].Split(':'); // extract Simvar name from format Simvar:Index
                    string sSimvarName = Array.Find(SimUtils.SimVars.Names, s => s == aSimvarSubStrings[0]);
                    string sUnitName = Array.Find(SimUtils.Units.Names, s => s == aSubStrings[1]);
                    if (sSimvarName != null && sUnitName != null)
                    {
                        AddRequest(aSubStrings[0], aSubStrings[1]);
                    }
                    else
                    {
                        if (sSimvarName == null)
                        {
                            lErrorMessages.Add("l." + i.ToString() + " Wrong Simvar name : " + aSubStrings[0]);
                        }
                        if (sUnitName == null)
                        {
                            lErrorMessages.Add("l." + i.ToString() + " Wrong Unit name : " + aSubStrings[1]);
                        }
                    }
                }
                else
                {
                    lErrorMessages.Add("l." + i.ToString() + " Bad input format : " + aLines[i]);
                    lErrorMessages.Add("l." + i.ToString() + " Must be : SIMVAR,UNIT");
                }
            }
        }

        private void SaveFile(bool _bWriteValues)
        {
            Microsoft.Win32.SaveFileDialog oSaveFileDialog = new Microsoft.Win32.SaveFileDialog();
            oSaveFileDialog.Filter = "Simvars files (*.simvars)|*.simvars";
            if (oSaveFileDialog.ShowDialog() == true)
            {
                using (StreamWriter oStreamWriter = new StreamWriter(oSaveFileDialog.FileName, false))
                {
                    foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
                    {
                        // Format : Simvar,Unit
                        string sFormatedLine = oSimvarRequest.sName + "," + oSimvarRequest.sUnits;
                        if (bSaveValues)
                        {
                            sFormatedLine += ",  " + oSimvarRequest.dValue.ToString();
                        }
                        oStreamWriter.WriteLine(sFormatedLine);
                    }
                }
            }
        }

        public void SetTickSliderValue(int _iValue)
        {
            m_oTimer.Interval = new TimeSpan(0, 0, 0, 0, (int)(_iValue));
        }

        public void InsertThermal()
        {
            if (m_oSimConnect != null)
            {
                //m_oSimConnect.WeatherCreateThermal();
            }
        }
    }
}
