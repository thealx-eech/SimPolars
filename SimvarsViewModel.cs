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
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Simvars
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoResponse
    {
        public double AbsoluteTime;
        public double Altitude;
        public double AirspeedTrue;
        public double VerticalSpeed;
        public double Flaps;
        public double Weight;
    };

    public class FsDataReceivedEventArgs : EventArgs
    {
        /// <summary>
        /// The request id of the received data.
        /// </summary>
        public uint RequestId { get; set; }

        /// <summary>
        /// The data that was received.
        /// </summary>
        public object Data { get; set; }
    }

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
    }; // end class SimvarRequest


    public class SimvarsViewModel : BaseViewModel, IBaseSimConnectWrapper
    {

        // ******************************************************************
        // CLASS VARS
        //*******************************************************************

        private JObject settings;

        private static PlaneInfoResponse _planeInfoResponse;
        private static PlaneInfoResponse _planeInfoResponseOld;

        public event EventHandler<FsDataReceivedEventArgs> FsDataReceived;

        public static List<SetValueItem> SetValueItems = new List<SetValueItem>();

        #region IBaseSimConnectWrapper implementation

        /// User-defined win32 event
        public const int WM_USER_SIMCONNECT = 0x0402;

        /// Window handle
        private IntPtr m_hWnd = new IntPtr(0);

        /// SimConnect object
        private SimConnect m_oSimConnect = null;

        private bool captureActive = false;

        private Dictionary<int, double>[] capturedDataArray = new Dictionary<int, double>[24];

        // ******************************************************************
        // INTERFACE METHODS
        //*******************************************************************

        //    interface IBaseSimConnectWrapper
        //    {
        //        int GetUserSimConnectWinEvent();
        //        void ReceiveSimConnectMessage();
        //        void SetWindowHandle(IntPtr _hWnd);
        //        void LoadSettings(string _sFileName);
        //        void Disconnect();
        //        void AddFlightDataRequest();
        //    }

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

        public void LoadSettings(string _sFileName)
        {
            string settings_str = System.IO.File.ReadAllText(_sFileName);
            settings = JObject.Parse(settings_str);
            Console.WriteLine("JSON: "+settings["foo"]);
        }

        public void Disconnect()
        {
            Console.WriteLine("Disconnecting from SimConnect");

            m_oTimer.Stop();
            bOddTick = false;

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

        public void AddFlightDataRequest()
        {
            SimvarRequest oSimvarRequest = new SimvarRequest
            {
                eDef = (DEFINITION)m_iCurrentDefinition,
                eRequest = (REQUEST)m_iCurrentRequest,
                sName = "FLIGHT DATA",
                sUnits = "array",
                dValue = 0
            };

            oSimvarRequest.bPending = ! RegisterDataDefinition<PlaneInfoResponse>((DEFINITION)m_iCurrentDefinition);
            oSimvarRequest.bStillPending = oSimvarRequest.bPending;

            lSimvarRequests.Add(oSimvarRequest);

            Console.WriteLine("AddFlightDataRequest def" + m_iCurrentDefinition + " req" + m_iCurrentRequest);

            ++m_iCurrentDefinition;
            ++m_iCurrentRequest;
        }


        // ******************************************************************
        // CLASS METHODS
        //*******************************************************************

        private bool m_bConnected = false;

        private uint m_iCurrentDefinition = 0;
        private uint m_iCurrentRequest = 0;

        public bool bConnected
        {
            get { return m_bConnected; }
            private set { this.SetProperty(ref m_bConnected, value); }
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
        private double AbsoluteTimeDelta = 0;
        private bool? variableTimer = false;

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

                FsDataReceived += HandleReceivedFsData;

            }
            catch (COMException ex)
            {
                Console.WriteLine("Connection to KH failed: " + ex.Message);
            }
        }

        private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("SimConnect_OnRecvOpen, Connected to KH");

            sConnectButtonLabel = "Disconnect";
            bConnected = true;

            // Register pending requests
            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                if (oSimvarRequest.bPending)
                {
                    if (oSimvarRequest.eDef == 0)
                    {
                        oSimvarRequest.bPending = !RegisterDataDefinition<PlaneInfoResponse>(oSimvarRequest.eDef);
                        oSimvarRequest.bStillPending = oSimvarRequest.bPending;
                    }
                    else
                    {
                        oSimvarRequest.bPending = !RegisterToSimConnect(oSimvarRequest);
                        oSimvarRequest.bStillPending = oSimvarRequest.bPending;
                    }
                }
            }

            m_oTimer.Start();
            bOddTick = false;
        }

        
        private void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            try
            {
                //Console.WriteLine("Received request #" + e.RequestId + " value " + JsonConvert.SerializeObject(e.Data, Formatting.Indented));

                if (e.RequestId == 0) // FLIGHT DATA
                {
                    if (!captureActive)
                    {
                        //Console.WriteLine("FLIGHT DATA captureActive: " + captureActive);
                    }
                    _planeInfoResponseOld = _planeInfoResponse;
                    _planeInfoResponse = (PlaneInfoResponse)e.Data;
                    lSimvarRequests[0].bPending = false;
                    lSimvarRequests[0].bStillPending = false;
                    if (captureActive)
                    {
                        double AbsoluteTimeDelta = 0;
                        if (_planeInfoResponse.AbsoluteTime != 0 && _planeInfoResponseOld.AbsoluteTime != 0)
                        {
                            AbsoluteTimeDelta = _planeInfoResponse.AbsoluteTime - _planeInfoResponseOld.AbsoluteTime;
                        }


                        if (_planeInfoResponse.AirspeedTrue != 0 && 
                            _planeInfoResponseOld.AirspeedTrue != 0 && 
                            _planeInfoResponse.Altitude != 0 && 
                            _planeInfoResponseOld.Altitude != 0 && 
                            AbsoluteTimeDelta > 0.1 &&
                            AbsoluteTimeDelta < 0.12)
                        {
                            //Console.Write("Capture " + (int)_planeInfoResponse.AirspeedTrue + " / " + sink);
                            if (capturedDataArray[(int)_planeInfoResponse.Flaps] == null)
                                capturedDataArray[(int)_planeInfoResponse.Flaps] = new Dictionary<int, double>();

                            //double te_raw_ms = getTeValue(_planeInfoResponseOld.Altitude, _planeInfoResponse.Altitude, _planeInfoResponseOld.AirspeedTrue, _planeInfoResponse.AirspeedTrue, AbsoluteTimeDelta);
                            double vertical_speed = (_planeInfoResponse.Altitude - _planeInfoResponseOld.Altitude) / AbsoluteTimeDelta;
                            double te_compensation = (Math.Pow(_planeInfoResponse.AirspeedTrue, 2) - Math.Pow(_planeInfoResponseOld.AirspeedTrue, 2)) / (2 * AbsoluteTimeDelta * 9.80665);
                            double te_raw_ms = vertical_speed + te_compensation;
                            Console.WriteLine(String.Format("{0:n6} : {1:n3} @ {2:n3} / {3:n3} @ {4:n3} = {5:n2} ( {6:n2} + {7:n2} )",
                                                AbsoluteTimeDelta,
                                                _planeInfoResponseOld.AirspeedTrue,
                                                _planeInfoResponseOld.Altitude,
                                                _planeInfoResponse.AirspeedTrue,
                                                _planeInfoResponse.Altitude,
                                                te_raw_ms,
                                                vertical_speed,
                                                te_compensation
                                                ));
                            capturedDataArray[(int)_planeInfoResponse.Flaps][(int)(_planeInfoResponse.AirspeedTrue)] = capturedDataArray[(int)_planeInfoResponse.Flaps].ContainsKey((int)_planeInfoResponse.AirspeedTrue)
                                ? 0.98 * capturedDataArray[(int)_planeInfoResponse.Flaps][(int)_planeInfoResponse.AirspeedTrue] + 0.02 * te_raw_ms : te_raw_ms;

                            ToggleRender();

                            if (variableTimer == true)
                            {
                                SetVariableTiming(_planeInfoResponse.AirspeedTrue, _planeInfoResponseOld.AirspeedTrue, AbsoluteTimeDelta);
                            }
                        }
                    }

                }
                else // CUSTOM VARS
                {
                    foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
                    {
                        if (e.RequestId == (uint)oSimvarRequest.eRequest)
                        {
                            double dValue = (double)e.Data;
                            oSimvarRequest.dValue = dValue;

                            oSimvarRequest.bPending = false;
                            oSimvarRequest.bStillPending = false;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not handle received FS data: " + ex);
            }
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
            FsDataReceived?.Invoke(this, new FsDataReceivedEventArgs()
            {
                RequestId = data.dwRequestID,
                Data = data.dwData[0]
            });
        }
        // See SimConnect.RequestDataOnSimObject
        private void OnTick(object sender, EventArgs e)
        {
            bOddTick = !bOddTick;
            if (_planeInfoResponse.AbsoluteTime != 0 && _planeInfoResponseOld.AbsoluteTime != 0)
            {
                AbsoluteTimeDelta = _planeInfoResponse.AbsoluteTime - _planeInfoResponseOld.AbsoluteTime;
            }

            //Console.WriteLine("READING DATA");

            foreach (SimvarRequest oSimvarRequest in lSimvarRequests)
            {
                if (!oSimvarRequest.bPending)
                {
                    //Console.WriteLine("Send request for #" + oSimvarRequest.eDef);
                    m_oSimConnect?.RequestDataOnSimObjectType(oSimvarRequest.eRequest, oSimvarRequest.eDef, 0, m_eSimObjectType);
                    oSimvarRequest.bPending = true;
                }
                else
                {
                    //Console.WriteLine("Still waiting respond for #" + oSimvarRequest.eDef);
                    oSimvarRequest.bStillPending = true;
                }
            }

            //Console.WriteLine("WRITING DATA");

            if (SetValueItems.Count > 0)
            {
                //Console.WriteLine("Updating " + SetValueItems.Count + " variables");
                foreach (SetValueItem itm in SetValueItems)
                {
                    m_oSimConnect.SetDataOnSimObject(itm.DefineID, itm.ObjectID, itm.Flags, itm.pDataSet);
                }
            }

            //Console.WriteLine("AERODYNAMICS CAPTURE");



            //Console.WriteLine("SAVE PREVIOUS STATE");
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

                    // TODO: RESET BASE FLIGHT PARAMETERS
                    //try { m_oSimConnect.SetDataOnSimObject(getSimvarId("PLANE PITCH DEGREES"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }
                    //try { m_oSimConnect.SetDataOnSimObject(getSimvarId("PLANE BANK DEGREES"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }
                    //try { m_oSimConnect.SetDataOnSimObject(getSimvarId("VELOCITY BODY Z"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)200); } catch (Exception e) { Console.WriteLine(e.Message); }
                    //try { m_oSimConnect.SetDataOnSimObject(getSimvarId("FUEL TANK CENTER LEVEL:1"), SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, (object)0); } catch (Exception e) { Console.WriteLine(e.Message); }

                    captureActive = true;
                    sCaptureButtonLabel = "DISABLE DATA CAPTURE";
                }
            }
        }

        private void ToggleClear()
        {
            if (_planeInfoResponse.Flaps != 0) // CLEAR FLAP DATA ONLY
            {
                capturedDataArray[(int)_planeInfoResponse.Flaps] = new Dictionary<int, double>();
            } else // CLEAR ALL
            {
                capturedDataArray = new Dictionary<int, double>[24];
            }

            ToggleRender();
        }
        public void ToggleRender()
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
                /*if (captureActive == true && airspeedTrue * 3.6 < valXstart) // DISABLE CAPTURE
                {
                    ToggleCapture();
                }*/

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
                foreach (var capturedData in capturedDataArray)
                {
                    if (capturedData != null && capturedData.Count > 0)
                    {
                        Color color = Color.FromRgb((byte)(255 - index % 4 * 127), (byte)(255 - (index + 1) % 3 * 127), (byte)(255 - (index + 2) % 3 * 127));
                        //Console.WriteLine(color.ToString());
                        parent.captureLabels.Children.Add(getTextLabel(color, "Flaps position #" + (index - 1) + " " + (int)_planeInfoResponse.Weight + "kg"));

                        foreach (var capturedValue in capturedData)
                        {
                            double airspeed = capturedValue.Key * 3.6;
                            double te = capturedValue.Value;
                            if (airspeed >= valXstart && airspeed <= valXend && (-te) >= valYstart && (-te) <= valYend)
                            {
                                parent.captureCanvas.Children.Add(getGraphLine( color,                                               // color
                                                                                (airspeed + 0.01 - Math.Ceiling(valXstart)) * unitX, // x1
                                                                                (-te + 0.02 - valYstart) * unitY,      // y1
                                                                                (airspeed - 0.01 - Math.Ceiling(valXstart)) * unitX, // x2 
                                                                                (-te - 0.02 - valYstart) * unitY,      // y2
                                                                                canvasWidth,                                         // width
                                                                                canvasHeight,                                        // height
                                                                                3));                                                 // thickness
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

        public bool RegisterDataDefinition<T>(Enum id) where T : struct
        {
            if (m_oSimConnect != null)
            {
                foreach (var simvar_request in getSimvarRequests((DEFINITION)id, (REQUEST)id))
                {
                    m_oSimConnect.AddToDataDefinition(id, simvar_request.sName, simvar_request.sUnits, SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                }

                m_oSimConnect.RegisterDataDefineStruct<T>(id);

                return true;
            }
            else
            {
                return false;
            }
        }

        // Returns a pre-defined list of SimvarRequest objects
        private List<SimvarRequest> getSimvarRequests(DEFINITION definition, REQUEST request)
        {
            List<SimvarRequest> oSimvarRequests = new List<SimvarRequest>();

            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "ABSOLUTE TIME", sUnits = "second", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "PLANE ALTITUDE", sUnits = "meters", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "AIRSPEED TRUE", sUnits = "meters per second", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "VERTICAL SPEED", sUnits = "meters per second", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "FLAPS HANDLE INDEX", sUnits = "enum", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "TOTAL WEIGHT", sUnits = "kilogram", dValue = 0 });

            return oSimvarRequests;
        }

        private void AddRequest(string _sOverrideSimvarRequest, string _sOverrideUnitRequest, double value = 0)
        {
            Console.WriteLine("AddRequest " + _sOverrideSimvarRequest + " " + _sOverrideUnitRequest + " def" + m_iCurrentDefinition + " req" + m_iCurrentRequest);

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

        public void SetTickMode(bool? variable)
        {
            variableTimer = variable;

            if (variableTimer == false)
            {
                parent = (MainWindow)System.Windows.Application.Current.MainWindow;
                SetTickSliderValue((int)(parent.sl_Tick.Value));
            }
        }

        public void SetVariableTiming(double airspeed, double airspeedOld, double interval)
        {
            double accel = Math.Abs(airspeed - airspeedOld) / interval;
            accel = Math.Max(accel, 0.05);
            accel = Math.Min(accel, 0.5);
            double multiplier = 1.0;
            int timer = 500;

            if (Math.Abs(airspeed) < 30)
                multiplier = 1 - Math.Abs(airspeed) / 30;

            timer = (int)(1000 * (1.1 - 2 * accel * multiplier));

            Console.WriteLine("Variable timer: " + timer + " acceleration: " + accel);
            SetTickSliderValue(timer);
        }
        public void InsertThermal()
        {
            if (m_oSimConnect != null)
            {
                //m_oSimConnect.WeatherCreateThermal();
            }
        }
    } // end class SimvarsViewModel
}
