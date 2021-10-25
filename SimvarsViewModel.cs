using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using System.Windows.Input;

namespace Simvars
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneInfoResponse
    {
        public double AbsoluteTime;
        public double Altitude;
        public double AirspeedTrue;
        public double AirspeedIndicated;
        public double VerticalSpeed;
        public double Flaps;
        public double FlapsNum;
        public double Weight;
        public double WingArea;
        public double Pitch;
        public double Bank;
        public double AoA;
        public double rotationVelocityX;
    };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct PlaneControlsCommit
    {
        public double Elevator;
        public double Aileron;
    }

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
        Dummy = 0,
        Commit = 1
    };

    public enum REQUEST
    {
        Dummy = 0,
        Commit = 1
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

        public string BASE_DIRECTORY = "";

        public JObject settings;

        public event EventHandler<FsDataReceivedEventArgs> FsDataReceived;

        public static List<SetValueItem> SetValueItems = new List<SetValueItem>();

        private const int POLLING_INTERVAL = 300; // Timer loop will issue data request every 300 ms.

        private ImageBrush background_ib; // Polar image
        private bool background_available = false;

        private static PlaneInfoResponse _planeInfoResponse;
        private static PlaneInfoResponse _planeInfoResponseOld;

        private static PlaneControlsCommit _planeControlsCommit;

        #region IBaseSimConnectWrapper implementation

        /// User-defined win32 event
        public const int WM_USER_SIMCONNECT = 0x0402;

        /// Window handle
        private IntPtr m_hWnd = new IntPtr(0);

        /// SimConnect object
        private SimConnect m_oSimConnect = null;


        private bool captureActive = true;
        private double autotrimCounter = 0;

        private Dictionary<int, double>[] capturedDataArray = new Dictionary<int, double>[24];

        private double canvasUnitX; // Initilized in SimvarsViewModel constructor.
        private double canvasUnitY;

        Point[] controlPoints;

        // ****************************************************************************************************
        // ****************************************************************************************************
        // ******* MAIN CLASS CONSTRUCTOR         *************************************************************
        // ****************************************************************************************************
        // ****************************************************************************************************
        public SimvarsViewModel(MainWindow parent_window)
        {
            parent = parent_window; // (MainWindow)System.Windows.Application.Current.MainWindow;
            lObjectIDs = new ObservableCollection<uint>();
            lObjectIDs.Add(1);

            BASE_DIRECTORY = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

            //***********************************************************************
            // Load settings from settings.json if available, otherwise use defaults
            //***********************************************************************
            string settings_file = BASE_DIRECTORY + "\\settings.json";
            if (File.Exists(settings_file))
            {
                LoadSettings(settings_file);
            }
            else
            {
                settings = new JObject();
            }

            // ADD MISSING VARIABLES
            foreach (string variable in new string[] { "airspeed_min_kph", "airspeed_max_kph", "sink_min_ms", "sink_max_ms", "polar_image", "speed_measurement", "precision", "forceHorizontalFlight", "hidePoints", "curveResolution", "stallBreakpoint" })
            {
                if (!settings.ContainsKey(variable))
                    settings.Add(variable, 1);
            }

            // Setting up background polar image
            background_ib = new ImageBrush();
            background_ib.Stretch = Stretch.Fill;

            handleSettingsChange();

            lSimvarRequests = new ObservableCollection<SimvarRequest>();
            lErrorMessages = new ObservableCollection<string>();

            cmdToggleConnect = new BaseCommand((p) => { ToggleConnect(); });
            cmdToggleCapture = new BaseCommand((p) => { ToggleCapture(); });
            cmdToggleReset = new BaseCommand((p) => { ToggleReset(); });
            cmdToggleResetFlap = new BaseCommand((p) => { ToggleResetFlap(); });
            cmdToggleLoad = new BaseCommand((p) => { ToggleLoad(); });
            cmdToggleSave = new BaseCommand((p) => { ToggleSave(); });
            cmdToggleSaveFlap = new BaseCommand((p) => { ToggleSave((int) _planeInfoResponse.Flaps); });
            cmdAddRequest = new BaseCommand((p) => { AddRequest(null, null); });
            cmdRemoveSelectedRequest = new BaseCommand((p) => { RemoveSelectedRequest(); });
            cmdToggleAverageFlapResult = new BaseCommand((p) => { ToggleAverageFlapResult(); });
            cmdTrySetValue = new BaseCommand((p) => { TrySetValue(); });
            cmdSetValuePerm = new BaseCommand((p) => { SetValuePerm(); });
            cmdLoadFiles = new BaseCommand((p) => { LoadFiles(); });
            cmdSaveFile = new BaseCommand((p) => { SaveFile(false); });
            cmdSaveSettings = new BaseCommand((p) => { SaveSettings(); });

            m_oTimer.Interval = new TimeSpan(0, 0, 0, 0, POLLING_INTERVAL);
            m_oTimer.Tick += new EventHandler(OnTick);
        }


        // ******************************************************************
        // INTERFACE METHODS
        //*******************************************************************

        //    interface IBaseSimConnectWrapper
        //    {
        //        int GetUserSimConnectWinEvent();
        //        void ReceiveSimConnectMessage();
        //        void SetWindowHandle(IntPtr _hWnd);
        //        void Disconnect();
        //        void AddFlightDataRequest();
        //        string getBaseDirectory();
        //        JObject getSettings();
        //    }

        public int GetUserSimConnectWinEvent()
        {
            return WM_USER_SIMCONNECT;
        }

        public string getBaseDirectory()
        {
            return BASE_DIRECTORY;
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
                eDef = DEFINITION.Dummy,
                eRequest = REQUEST.Dummy,
                sName = "FLIGHT DATA",
                sUnits = "array",
                dValue = 0
            };

            oSimvarRequest.bPending = !RegisterDataDefinition<PlaneInfoResponse>(DEFINITION.Dummy, getSimvarRequests(DEFINITION.Dummy, REQUEST.Dummy));
            oSimvarRequest.bStillPending = oSimvarRequest.bPending;

            lSimvarRequests.Add(oSimvarRequest);

            Console.WriteLine("AddFlightDataRequest def" + m_iCurrentDefinition + " req" + m_iCurrentRequest);

            ++m_iCurrentDefinition;
            ++m_iCurrentRequest;

            oSimvarRequest = new SimvarRequest
            {
                eDef = DEFINITION.Commit,
                eRequest = REQUEST.Commit,
                sName = "CONSTROLS COMMIT",
                sUnits = "array",
                dValue = 0
            };

            
            oSimvarRequest.bPending = !RegisterDataDefinition<PlaneControlsCommit>(DEFINITION.Commit, getCommitRequests(DEFINITION.Commit, REQUEST.Commit));
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
        public BaseCommand cmdToggleReset { get; private set; }
        public BaseCommand cmdToggleResetFlap { get; private set; }
        public BaseCommand cmdToggleLoad { get; private set; }
        public BaseCommand cmdToggleSave { get; private set; }
        public BaseCommand cmdToggleSaveFlap { get; private set; }
        public BaseCommand cmdToggleAverageFlapResult { get; private set; }
        public BaseCommand cmdSaveSettings { get; private set; }
        public BaseCommand cmdAddRequest { get; private set; }
        public BaseCommand cmdRemoveSelectedRequest { get; private set; }
        public BaseCommand cmdTrySetValue { get; private set; }
        public BaseCommand cmdSetValuePerm { get; private set; }
        public BaseCommand cmdLoadFiles { get; private set; }
        public BaseCommand cmdSaveFile { get; private set; }

        public MainWindow parent;

        static double vsCompensation = 0;

        #endregion

        #region Real time

        private DispatcherTimer m_oTimer = new DispatcherTimer();
        private double AbsoluteTimeDelta = 0;
        private bool? variableTimer = false;

        #endregion

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

        void ToggleAverageFlapResult()
        {
            Render(0, true);
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
                    if (oSimvarRequest.eDef == DEFINITION.Dummy)
                    {
                        oSimvarRequest.bPending = !RegisterDataDefinition<PlaneInfoResponse>(oSimvarRequest.eDef, getSimvarRequests(oSimvarRequest.eDef, oSimvarRequest.eRequest));
                        oSimvarRequest.bStillPending = oSimvarRequest.bPending;
                    }
                    else if (oSimvarRequest.eDef == DEFINITION.Commit)
                    {
                        oSimvarRequest.bPending = !RegisterDataDefinition<PlaneInfoResponse>(oSimvarRequest.eDef, getCommitRequests(oSimvarRequest.eDef, oSimvarRequest.eRequest));
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

        // Return the 'data collection bucket' for a given speed (over which the TE will be smoothed)
        private int bucket(double speed_ms)
        {
            return (int)speed_ms;
        }

        // *********************************************************************************
        // HandleReceivedFsData (called each time new SimConnect data is available)
        // *********************************************************************************
        private void HandleReceivedFsData(object sender, FsDataReceivedEventArgs e)
        {
            try
            {
                parent = (MainWindow)System.Windows.Application.Current.MainWindow;
                //Console.WriteLine("Received request #" + e.RequestId + " value " + JsonConvert.SerializeObject(e.Data, Formatting.Indented));

                if (e.RequestId == 0) // FLIGHT DATA
                {
                    _planeInfoResponseOld = _planeInfoResponse;
                    _planeInfoResponse = (PlaneInfoResponse)e.Data;
                    lSimvarRequests[0].bPending = false;
                    lSimvarRequests[0].bStillPending = false;

                    if (captureActive)
                    {
                        // RENDER FLAPS
                        if (_planeInfoResponse.FlapsNum != 0)
                        {
                            addFlapLegend((int)_planeInfoResponse.FlapsNum, (int)_planeInfoResponse.Flaps);
                        }

                        double AbsoluteTimeDelta = 0;
                        if (_planeInfoResponse.AbsoluteTime != 0 && _planeInfoResponseOld.AbsoluteTime != 0)
                        {
                            AbsoluteTimeDelta = _planeInfoResponse.AbsoluteTime - _planeInfoResponseOld.AbsoluteTime;
                        }

                        double airspeed_old = settings.GetValue("speed_measurement").ToString() == "0" ? _planeInfoResponseOld.AirspeedTrue : _planeInfoResponseOld.AirspeedIndicated;
                        double airspeed_ms = settings.GetValue("speed_measurement").ToString() == "0" ? _planeInfoResponse.AirspeedTrue : _planeInfoResponse.AirspeedIndicated;

                        if (airspeed_ms != airspeed_old &&
                            airspeed_ms != 0 &&
                            airspeed_old != 0 &&
                            _planeInfoResponse.Altitude != 0 &&
                            _planeInfoResponseOld.Altitude != 0)// &&
                            //AbsoluteTimeDelta > 0.1 &&
                            //AbsoluteTimeDelta < 0.12)
                        {
                            //Console.WriteLine("FLIGHT DATA captureActive: " + captureActive);

                            //Console.Write("Capture " + (int)_planeInfoResponse.AirspeedTrue + " / " + sink);
                            if (capturedDataArray[(int)_planeInfoResponse.Flaps] == null)
                            {
                                capturedDataArray[(int)_planeInfoResponse.Flaps] = new Dictionary<int, double>();
                            }
                            //double te_raw_ms = getTeValue(_planeInfoResponseOld.Altitude, _planeInfoResponse.Altitude, _planeInfoResponseOld.AirspeedIndicated, _planeInfoResponse.AirspeedIndicated, AbsoluteTimeDelta);
                            double vertical_speed = (_planeInfoResponse.Altitude - _planeInfoResponseOld.Altitude) / AbsoluteTimeDelta;
                            double te_compensation = (Math.Pow(airspeed_ms, 2) - Math.Pow(airspeed_old, 2)) / (2 * AbsoluteTimeDelta * 9.80665);
                            double te_raw_ms = vertical_speed + te_compensation;
                            if (settings.GetValue("speed_measurement").ToString() != "0") {
                                te_raw_ms *= _planeInfoResponse.AirspeedIndicated / _planeInfoResponse.AirspeedTrue;
                            }
                            double glide_ratio = te_raw_ms > -0.1 ? 99 : airspeed_ms / -te_raw_ms;
                            /*Console.WriteLine(String.Format("{0:n6} s,{1,7:n3} kph @ ,{2:n3} m / {3,7:n3} kph @ {4:n3} m = te:{5,6:n2}, vsi: {6,6:n2}, + comp {7,6:n2} ) L/D={8,5:n1}, flap={9}, weight={10}kg",
                                                AbsoluteTimeDelta,
                                                airspeed_ms * 3.6, // m/s -> kph
                                                _planeInfoResponseOld.Altitude,
                                                airspeed_ms * 3.6,
                                                _planeInfoResponse.Altitude,
                                                te_raw_ms,
                                                vertical_speed,
                                                te_compensation,
                                                glide_ratio,
                                                (int)_planeInfoResponse.Flaps,
                                                (int)_planeInfoResponse.Weight
                                                ));*/

                            //if (glide_ratio > 5 && glide_ratio < 100)
                            if (airspeed_ms * 3.6 >= (double)settings.GetValue("airspeed_min_kph") && airspeed_ms * 3.6 <= (double)settings.GetValue("airspeed_max_kph") &&
                                -te_raw_ms > 0 && -te_raw_ms <= (double)settings.GetValue("sink_max_ms"))
                            {
                                int airspeed_bucket = bucket(airspeed_ms / (double)settings.GetValue("precision"));

                                // Alternative Smoothing ratio is variable 0.7 .. 0.9 depending on acceleration (te_compensation)
                                double SMOOTHING_RATIO = 0.5; // Math.Min(0.7 + Math.Abs(te_compensation) * 0.2 / 3,0.9); // 0.9;

                                capturedDataArray[(int)_planeInfoResponse.Flaps][airspeed_bucket] = capturedDataArray[(int)_planeInfoResponse.Flaps].ContainsKey(airspeed_bucket)
                                    ? SMOOTHING_RATIO * capturedDataArray[(int)_planeInfoResponse.Flaps][airspeed_bucket] + (1 - SMOOTHING_RATIO) * te_raw_ms : te_raw_ms;

                                Render(airspeed_ms * 3.6);

                                //Console.WriteLine("FLIGHT DATA captured value: " + airspeed_bucket);
                            }
                            else
                            {
                                //Console.WriteLine("FLIGHT DATA capture skipped, glide_ratio: " + glide_ratio);
                            }

                            if (variableTimer == true)
                            {
                                SetVariableTiming(airspeed_ms, airspeed_old, AbsoluteTimeDelta);
                            }

                            // FORCE HORIZINTAL FLIGHT
                            if ((string)settings["forceHorizontalFlight"] == "true" && AbsoluteTimeDelta != 0 && Math.Abs(_planeInfoResponse.Pitch) < 10 && Math.Abs(_planeInfoResponse.rotationVelocityX) < 10)
                            {
                                parent.forceHorizontalFlight.Background = new SolidColorBrush(Colors.LightGreen);

                                vsCompensation -= Math.Sign(_planeInfoResponse.VerticalSpeed) *  Math.Pow(Math.Abs(_planeInfoResponse.VerticalSpeed), 0.5) * AbsoluteTimeDelta;
                                vsCompensation = Math.Max(-50, Math.Min(50, vsCompensation));

                                // STALL
                                //if (captureActive == true && airspeed_ms * 3.6 < 100 && glide_ratio < 10) // DISABLE CAPTURE
                                if (180 - Math.Abs(_planeInfoResponse.AoA) > (double)settings.GetValue("stallBreakpoint"))
                                {
                                    _planeControlsCommit.Aileron = 0;
                                    _planeControlsCommit.Elevator = 0;

                                    m_oSimConnect.SetDataOnSimObject(DEFINITION.Commit, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, _planeControlsCommit);

                                    //parent.forceHorizontalFlight.IsChecked = false;
                                    //updateSetting("forceHorizontalFlight", "false");
                                    ToggleConnect();
                                }
                                // AUTOTRIM
                                else
                                {
                                    double trimLimit = 50;//airspeed_ms * 3.6 < 100 ? 50 : 7.5;
                                    double newAileron = Math.Max(-trimLimit, Math.Min(trimLimit, Math.Sign(_planeInfoResponse.Bank) * Math.Pow(Math.Abs(_planeInfoResponse.Bank), 2)));
                                    double newElevator = Math.Max(-trimLimit, Math.Min(trimLimit, 2 * _planeInfoResponse.Pitch + 1 + vsCompensation));

                                    _planeControlsCommit.Aileron = newAileron;
                                    double elevatorDelta = newElevator - _planeControlsCommit.Elevator;

                                    // RESET TO ZERO
                                    if (_planeControlsCommit.Elevator < 0 && newElevator > 0 || _planeControlsCommit.Elevator > 0 && newElevator < 0)
                                        _planeControlsCommit.Elevator = 0;
                                    else
                                        _planeControlsCommit.Elevator += Math.Min(Math.Max(elevatorDelta * autotrimCounter, -10), 10) * AbsoluteTimeDelta;

                                    if (_planeControlsCommit.Elevator < -10)
                                    {
                                        _planeControlsCommit.Elevator = -10;
                                    }

                                    m_oSimConnect.SetDataOnSimObject(DEFINITION.Commit, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, _planeControlsCommit);
                                }

                                if (autotrimCounter < 1)
                                    autotrimCounter = Math.Min(1, autotrimCounter + AbsoluteTimeDelta / 5);
                            }
                            else
                            {
                                parent.forceHorizontalFlight.Background = new SolidColorBrush(Colors.LightPink);
                            }
                        }

                    }

                }
                else if (e.RequestId == 1)
                {
                    _planeControlsCommit = (PlaneControlsCommit)e.Data;
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

        // ************************
        // Update the display
        // ************************
        private double speed_kph_to_x(double speed_kph)
        {
            return (speed_kph - (double)settings.GetValue("airspeed_min_kph")) * canvasUnitX;
        }

        private double sink_ms_to_y(double sink_ms)
        {
            // Note returned value is proportion of height of canvas, not absolute pixels
            return -(sink_ms + (double)settings.GetValue("sink_min_ms")) * canvasUnitY;
        }

        public void Render(double current_speed_kph, bool stall_line = false)
        {
            parent = (MainWindow)System.Windows.Application.Current.MainWindow;
            if (parent.captureCanvas != null)
            {
                parent.captureCanvas.Children.Clear();
                parent.captureCanvas.Focus();

                double canvasWidth = parent.captureCanvas.Width;
                double canvasHeight = parent.captureCanvas.Height;
                parent.captureCanvas.Children.Add(getGraphLine(Colors.Blue, 0, 0, 0, 1, canvasWidth, canvasHeight, 2));
                parent.captureCanvas.Children.Add(getGraphLine(Colors.Blue, 0, 0, 1, 0, canvasWidth, canvasHeight, 2));

                // BACKGROUND IMAGE
                //if (!string.IsNullOrEmpty(parent.graphBgImagePath.Text) && File.Exists(parent.graphBgImagePath.Text))
                if (background_available)
                {
                    //Image img = new Image();
                    parent.captureCanvas.Background = background_ib;
                }
                else
                {
                    parent.captureCanvas.Background = null;
                }

                // AIRSPEED GRID LINES (vertical)
                for (double speed_kph = 0; speed_kph <= (double)settings.GetValue("airspeed_max_kph"); speed_kph += 10)
                {
                    if (speed_kph > (double)settings.GetValue("airspeed_min_kph"))
                    {
                        double x = speed_kph_to_x(speed_kph);
                        parent.captureCanvas.Children.Add(getGraphLine(Colors.LightBlue, x, 0, x, 1, canvasWidth, canvasHeight, 1));
                        getCanvasTextLabel(parent.captureCanvas, Colors.Gray, x * canvasWidth, -16, speed_kph.ToString());
                    }
                }

                // SINK GRID LINES (horizontal)
                for (double sink_ms = 0; sink_ms >= -(double)settings.GetValue("sink_max_ms"); sink_ms -= 0.2)
                //for (double k = 0; k <= (double)settings.GetValue("sink_max_ms") - Math.Ceiling((double)settings.GetValue("sink_min_ms")); k += 1)
                {
                    double y = sink_ms_to_y(sink_ms);
                    // Horizontal grid line
                    parent.captureCanvas.Children.Add(getGraphLine(Colors.LightBlue, 0, y, 1, y, canvasWidth, canvasHeight, 1));
                    // Sink value at top of line
                    getCanvasTextLabel(parent.captureCanvas, Colors.Gray, -24, y * canvasHeight - 10, sink_ms.ToString());
                }

                // Legend label
                updateLegendLabel();

                // CAPTURED VALUES
                int index = 1;
                foreach (var capturedData in capturedDataArray)
                {
                    if (capturedData != null && capturedData.Count > 0)
                    {
                        int vindex = 0;

                        Color color = getFlapColor(index - 1);
                        color.A = 128;

                        double prevTE = -1;

                        SortedDictionary<int, double> sortedCapturedData = new SortedDictionary<int, double>(capturedData);
                        Point[] points = new Point[sortedCapturedData.Count];
                        foreach (var capturedValue in sortedCapturedData)
                        {
                            double airspeed_kph = capturedValue.Key * 3.6 * (double)settings.GetValue("precision");
                            double te = capturedValue.Value;

                            double x1 = (airspeed_kph + 0.01 - Math.Ceiling((double)settings.GetValue("airspeed_min_kph"))) * canvasUnitX;
                            double x2 = (airspeed_kph + 0.6 - Math.Ceiling((double)settings.GetValue("airspeed_min_kph"))) * canvasUnitX;
                            double y1 = (-te + 0.02 - (double)settings.GetValue("sink_min_ms")) * canvasUnitY;
                            double y2 = (-te + 0.02 - (double)settings.GetValue("sink_min_ms")) * canvasUnitY;

                            if (((string)settings.GetValue("hidePoints") != "true" || !stall_line) && airspeed_kph >= (double)settings.GetValue("airspeed_min_kph") && airspeed_kph <= (double)settings.GetValue("airspeed_max_kph") && (-te) >= (double)settings.GetValue("sink_min_ms") && (-te) <= (double)settings.GetValue("sink_max_ms"))
                            {
                                Line point = getGraphLine(color,                                               // color
                                                                                x1,
                                                                                y1,
                                                                                x2,
                                                                                y2,
                                                                                canvasWidth,                                         // width
                                                                                canvasHeight,                                        // height
                                                                                3);                                                 // thickness

                                point.Tag = (index-1) + "-" + capturedValue.Key;
                                point.MouseDown += new MouseButtonEventHandler(removePoint);
                                point.Cursor = Cursors.No;
                                parent.captureCanvas.Children.Add(point);
                                //Console.WriteLine(airspeed + " " + te + " / " + (airspeed - Math.Ceiling((double)settings.GetValue("airspeed_min_kph"))) * canvasUnitX + " " + (-te - Math.Ceiling(canvasYstart)) * canvasUnitY);
                            }

                            prevTE = te;

                            // PREPARE CURVE DATA
                            if (stall_line)
                            {
                                points[vindex] = new Point(x1 * canvasWidth, y1 * canvasHeight);
                                Console.WriteLine($"Point added: {x1 * canvasWidth:f2} {y1 * canvasHeight:F2}");
                            }

                            vindex++;
                        }


                        // RENDER CURVE
                        if (stall_line)
                        {
                            Console.WriteLine("a");
                            controlPoints = points;
                            var b = GetBezierApproximation((int)settings.GetValue("curveResolution"));
                            Console.WriteLine("b");
                            PathFigure pf = new PathFigure(b.Points[0], new[] { b }, false);
                            PathFigureCollection pfc = new PathFigureCollection();
                            pfc.Add(pf);
                            Console.WriteLine("c");
                            var pge = new PathGeometry();
                            pge.Figures = pfc;
                            System.Windows.Shapes.Path p = new System.Windows.Shapes.Path();
                            Console.WriteLine("d");
                            p.Data = pge;
                            color.A = 255;
                            p.Stroke = new SolidColorBrush(color);
                            p.StrokeThickness = 2;
                            parent.captureCanvas.Children.Add(p);

                            // STALL CAPTURE

                            /*Point prevPoint = new Point();
                            foreach(Point pnt in b.Points)
                            {
                                if (prevPoint.X != 0 && prevPoint.Y != 0 && prevPoint.Y < pnt.Y)
                                {
                                    double stallSpeed = (double)settings.GetValue("stallBreakpoint") / 100 * prevPoint.X / canvasWidth;
                                    parent.captureCanvas.Children.Add(getGraphLine(color, stallSpeed, 0, stallSpeed, 2, canvasWidth, canvasHeight, 2));
                                    Console.WriteLine("STALL for FLAP#" + (index - 1) + ": " + stallSpeed + "km/h");
                                    break;
                                }

                                prevPoint = pnt;
                            }*/
                        }
                    }
                    index++;
                }

                // CURRENT SPEED
                double speed_x = speed_kph_to_x(current_speed_kph);
                // color x1 y1 x2 y2                             strokeWidth
                parent.captureCanvas.Children.Add(getGraphLine(Colors.Red, speed_x, 0, speed_x, 1, canvasWidth, canvasHeight, 1));
            }
        } // end Render()

        // The case where the user closes game
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

        private void ToggleResetFlap()
        {
            capturedDataArray[(int)_planeInfoResponse.Flaps] = new Dictionary<int, double>();
            Render(0);
        }


        private void ToggleReset()
        {
            if (MessageBox.Show("It will clear ALL data", "Are you sure?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                capturedDataArray = new Dictionary<int, double>[24];
                Render(0);
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

        private Color getFlapColor(int flap_index)
        {
            Color[] flap_colors = {
                Color.FromRgb(128, 0, 0),   // 0
                Color.FromRgb(255, 0, 0),   // 1
                Color.FromRgb(0, 100, 0),   // 2
                Color.FromRgb(235, 125, 0), // 3
                Color.FromRgb(0, 0,128),    // 4
                Color.FromRgb(255, 0, 255), // 5
                Color.FromRgb(255,215,0)    // 6
            };

            return flap_colors[flap_index % flap_colors.Length];
        }

        private void updateLegendLabel()
        {
            if (_planeInfoResponse.Weight > 0 && _planeInfoResponse.Weight > 0)
            {
                parent.LegendLabel.Text = (_planeInfoResponse.Weight / _planeInfoResponse.WingArea).ToString("#.#") + "kg/m2   Flaps:";
            }
            else
            {
                parent.LegendLabel.Text = (int)_planeInfoResponse.Weight + "kg Flaps:";
            }
        }

        private void addFlapLegend(int flaps, int current = -1)
        {
            parent.captureLabels.Children.Clear();
            for (int flap_index = 0; flap_index <= flaps; flap_index++)
            {
                Color color = getFlapColor(flap_index);

                Button legend_item = new Button();
                legend_item.Content = "Flap #" + flap_index;
                legend_item.Tag = flap_index;
                legend_item.Foreground = new SolidColorBrush(color);
                legend_item.Background = new SolidColorBrush(Colors.Transparent);
                legend_item.BorderThickness = new Thickness(0);

                legend_item.HorizontalAlignment = HorizontalAlignment.Center;
                Border legend_item_border = new Border();
                legend_item_border.Child = legend_item;

                if (current == flap_index)
                {
                    legend_item_border.BorderBrush = new SolidColorBrush(Colors.Red);
                    legend_item_border.BorderThickness = new Thickness(2);
                }

                legend_item.Cursor = Cursors.Hand;
                legend_item.Click += new RoutedEventHandler(flapChange);

                Grid.SetColumn(legend_item_border, flap_index);
                Grid.SetRow(legend_item_border, 0);

                //Console.WriteLine(color.ToString());
                parent.captureLabels.Children.Add(legend_item_border);
            }
        }
        void flapChange(Object sender, EventArgs e)
        {
            Button clickedButton = (Button)sender;
            if (clickedButton.Tag != null && int.TryParse(clickedButton.Tag.ToString(), out int flap)) {
                _planeInfoResponse.Flaps = flap;
                addFlapLegend((int)_planeInfoResponse.FlapsNum, (int)_planeInfoResponse.Flaps);
            }
        }

        private TextBlock getTextBlock(Color color, string text)
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
            if (MessageBox.Show("It will clear current flap data", "Are you sure?", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                int currentFlap = (int)_planeInfoResponse.Flaps;

                System.Windows.Forms.OpenFileDialog openFileDialog1 = new System.Windows.Forms.OpenFileDialog();
                openFileDialog1.Filter =
                    "JSON|*.JSON;|" +
                    "All files (*.*)|*.*";
                openFileDialog1.Multiselect = true;
                openFileDialog1.Title = "Select polar data files";

                List<Dictionary<int, double>> newData = new List<Dictionary<int, double>>();

                System.Windows.Forms.DialogResult dr = openFileDialog1.ShowDialog();
                // Set the file dialog to filter for graphics files.

                if (dr == System.Windows.Forms.DialogResult.OK)
                {
                    foreach (String file in openFileDialog1.FileNames)
                    {
                        try
                        {
                            newData.Add(JsonConvert.DeserializeObject<Dictionary<int, double>>(File.ReadAllText(file)));
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }

                    Dictionary<int, double> newDict = new Dictionary<int, double>();

                    for (int i = 0; i < 10000; i++)
                    {
                        double total = 0;
                        int count = 0;

                        foreach (Dictionary<int, double> data in newData)
                        {
                            if (data.ContainsKey(i))
                            {
                                total += data[i];
                                count++;
                            }
                        }

                        if (count > 0)
                            newDict.Add(i, total / count);
                    }

                    capturedDataArray[currentFlap] = newDict;
                    Render(0);
                }
            }
        }

        private void ToggleSave(int spoiler = -1)
        {
            int index = 0;
            int counter = 0;

            string date = DateTime.Now.ToString("dd.MM.yyyy");
            string time = DateTime.Now.ToString("HH-mm-ss");

            if (!Directory.Exists(BASE_DIRECTORY + "\\save_data " + date)) {
                try
                {
                    Directory.CreateDirectory(BASE_DIRECTORY + "\\save_data " + date);
                }
                catch(Exception ex) { MessageBox.Show(ex.Message); }
            }

            foreach (var capturedData in capturedDataArray)
            {
                if (capturedData != null && capturedData.Count > 0)
                {
                    if (spoiler == -1 || spoiler == index)
                    {
                        try
                        {
                            File.WriteAllText(BASE_DIRECTORY + "\\save_data " + date + "\\save_data_flap_" + index.ToString("00") + " " + time + ".json", JsonConvert.SerializeObject(capturedData));
                            counter++;
                        }
                        catch (Exception ex) { MessageBox.Show(ex.Message); }
                    }
                }

                index++;
            }

            MessageBox.Show(counter + " files saved into folder " + BASE_DIRECTORY + "\\save_data " + date + "\\");
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

        public bool RegisterDataDefinition<T>(Enum id, List<SimvarRequest> requests) where T : struct
        {
            if (m_oSimConnect != null)
            {
                foreach (var simvar_request in requests)
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
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "AIRSPEED INDICATED", sUnits = "meters per second", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "VERTICAL SPEED", sUnits = "meters per second", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "FLAPS HANDLE INDEX", sUnits = "enum", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "FLAPS NUM HANDLE POSITIONS", sUnits = "enum", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "TOTAL WEIGHT", sUnits = "kilogram", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "WING AREA", sUnits = "meters", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "PLANE PITCH DEGREES", sUnits = "degree", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "PLANE BANK DEGREES", sUnits = "degree", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "ANGLE OF ATTACK INDICATOR", sUnits = "degree", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "ROTATION VELOCITY BODY X", sUnits = "degree per second", dValue = 0 });

            return oSimvarRequests;
        }

        private List<SimvarRequest> getCommitRequests(DEFINITION definition, REQUEST request)
        {
            List<SimvarRequest> oSimvarRequests = new List<SimvarRequest>();

            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "ELEVATOR TRIM POSITION", sUnits = "degree", dValue = 0 });
            oSimvarRequests.Add(new SimvarRequest { eDef = definition, eRequest = request, sName = "AILERON POSITION", sUnits = "degree", dValue = 0 });
            
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

        // ***********************************************************
        // Settings
        // ***********************************************************

        // getSettings called by UI to populate display fields
        public JObject getSettings()
        {
            return settings;
        }

        // updateSetting called by UI when user changes input field
        public bool updateSetting(string setting_key, string setting_value)
        {
            Console.WriteLine("updateSetting " + setting_key + " to " + setting_value);

            if (!settings.ContainsKey(setting_key))
                settings.Add(setting_key);

            switch (setting_key)
            {
                case "airspeed_min_kph":
                case "airspeed_max_kph":
                case "sink_min_ms":
                case "sink_max_ms":
                case "precision":
                case "stallBreakpoint":
                    settings[setting_key] = double.Parse(setting_value);
                    break;
                case "polar_image":
                    settings[setting_key] = setting_value;
                    break;
                case "forceHorizontalFlight":
                case "hidePoints":
                    settings[setting_key] = setting_value;
                    autotrimCounter = 0;
                    break;
                case "speed_measurement":
                case "curveResolution":
                    settings[setting_key] = int.Parse(setting_value);
                    break;
                default:
                    Console.WriteLine("How strange... we tried to update a setting we didn't recognize");
                    return false;
            }
            handleSettingsChange();
            Render(0);

            SaveSettings();

            return true;
        }

        private void handleSettingsChange()
        {
            if (settings.ContainsKey("polar_image"))
            {
                string polar_filename = (string)settings.GetValue("polar_image");

                if (!polar_filename.Contains(":") && !polar_filename.StartsWith("\\") && !polar_filename.StartsWith("."))
                {
                    polar_filename = BASE_DIRECTORY + "\\" + polar_filename;
                }

                if (File.Exists(polar_filename))
                {
                    try
                    {
                        background_ib.ImageSource = new BitmapImage(new Uri(polar_filename));
                        background_available = true;
                    }
                    catch
                    {
                        background_available = false;
                        MessageBox.Show("Polar image "+ polar_filename + " not a usable image format!");
                        Console.WriteLine("Polar image "+ polar_filename + " not a usable image format!");
                    }
                }
                else
                {
                    background_available = false;
                    Console.WriteLine("Polar image: " + polar_filename + " not found!");
                    MessageBox.Show("Polar image: "+polar_filename + " not found!");
                }
            }

            // Setup canvas scale
            canvasUnitX = 1 / ((double)settings.GetValue("airspeed_max_kph") - (double)settings.GetValue("airspeed_min_kph"));
            canvasUnitY = 1 / ((double)settings.GetValue("sink_max_ms") - (double)settings.GetValue("sink_min_ms"));
        }

        private void LoadSettings(string _sFileName)
        {
            string settings_str = System.IO.File.ReadAllText(_sFileName);
            settings = JObject.Parse(settings_str);

            Console.WriteLine("Settings loaded from " + _sFileName);
        }


        private void SaveSettings()
        {
            string settings_filename = BASE_DIRECTORY + "\\settings.json";
            Console.WriteLine("Saving settings to " + settings_filename);
            using (StreamWriter oStreamWriter = new StreamWriter(settings_filename, false))
            {
                string settings_str = settings.ToString();
                oStreamWriter.Write(settings_str);
            }
        }

        // ***********************************************************
        // Timer stuff
        // ***********************************************************

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

        Point[,,] BezierPoints;
        int pointsProcessed = 0;
        PolyLineSegment GetBezierApproximation(int outputSegmentCount)
        {
            Point[] points = new Point[outputSegmentCount + 1];
            BezierPoints = new Point[outputSegmentCount + 1, controlPoints.Length+1, controlPoints.Length+1];

            if (outputSegmentCount > 0)
            {
                for (int i = 0; i <= outputSegmentCount; i++)
                {
                    double t = (double)i / outputSegmentCount;

                    Console.WriteLine($"Starting segment: t {t:F3} " + DateTime.Now.ToString("HH:mm:ss"));

                    try
                    {
                        points[i] = GetBezierPoint(i, t, 0, controlPoints.Length);
                    }
                    catch (Exception e) {
                        Console.WriteLine(e.Message);
                    }

                    Console.WriteLine("Points processed: " + pointsProcessed);
                    pointsProcessed = 0;
                }
            }

            return new PolyLineSegment(points, true);
        }

        Point GetBezierPoint(int i, double t, int index, int count)
        {
            pointsProcessed++;
            //Console.WriteLine($"Interaction: t {t:F3} / index {index} / count {count} " + DateTime.Now.ToString("HH:mm:ss"));

            if (count == 1)
            {
                BezierPoints[i, index, count] = controlPoints[index];
                return controlPoints[index];
            }

            if (BezierPoints[i, index, count].X != 0 && BezierPoints[i, index, count].Y != 0)
                return BezierPoints[i, index, count];

            //System.Threading.Tasks.Parallel.For(0, 1, i =>
            //{
            Point P0 = count - 1 == 1 ? controlPoints[index] : GetBezierPoint(i, t, index, count - 1);
            Point P1 = count - 1 == 1 ? controlPoints[index + 1] : GetBezierPoint(i, t, index + 1, count - 1);
            //});

            Point result = new Point((1 - t) * P0.X + t * P1.X, (1 - t) * P0.Y + t * P1.Y);
            BezierPoints[i, index, count] = result;

            return result;
        }

        void removePoint(object sender, MouseButtonEventArgs e)
        {
            // Change line colour back to normal 
            string[] atts = ((Line)sender).Tag.ToString().Split('-');
            int index = int.Parse(atts[0]);
            int key = int.Parse(atts[1]);

            if (capturedDataArray.Length > index && capturedDataArray[index] != null && capturedDataArray[index].ContainsKey(key))
                capturedDataArray[index].Remove(key);
            else
            {
                Console.WriteLine("Failed to remove point " + index + " / " + key);
            }

            Render(0);
        }

    } // end class SimvarsViewModel

} // end namespace Simvars


