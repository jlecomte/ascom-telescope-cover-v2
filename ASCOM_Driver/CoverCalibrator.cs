/*
 * CoverCalibrator.cs
 * Copyright (C) 2024 - Present, Julien Lecomte - All Rights Reserved
 * Licensed under the MIT License. See the accompanying LICENSE file for terms.
 */

using ASCOM.DeviceInterface;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ASCOM.DarkSkyGeek
{
    //
    // Your driver's DeviceID is ASCOM.DarkSkyGeek.TelescopeCoverV2
    //
    // The Guid attribute sets the CLSID for ASCOM.DarkSkyGeek.TelescopeCoverV2
    // The ClassInterface/None attribute prevents an empty interface called
    // _DarkSkyGeek from being created and used as the [default] interface
    //

    /// <summary>
    /// DarkSkyGeek Telescope Cover & Spectral Calibrator ASCOM Driver.
    /// </summary>
    [Guid("6a7ab618-7bab-44c3-81eb-d1aac21decfd")]
    [ClassInterface(ClassInterfaceType.None)]
    public class TelescopeCoverV2 : ICoverCalibratorV1
    {
        /// <summary>
        /// ASCOM DeviceID (COM ProgID) for this driver.
        /// The DeviceID is used by ASCOM applications to load the driver at runtime.
        /// </summary>
        internal static string driverID = "ASCOM.DarkSkyGeek.TelescopeCoverV2";

        /// <summary>
        /// Driver description that displays in the ASCOM Chooser.
        /// </summary>
        private static readonly string deviceName = "DarkSkyGeek Telescope Cover & Spectral Calibrator";

        // Constants used for Profile persistence
        private const string autoDetectComPortProfileName = "Auto-Detect COM Port";
        private const string autoDetectComPortDefault = "true";

        private const string comPortProfileName = "COM Port";
        private const string comPortDefault = "COM1";

        private const string lastComPortProfileName = "Last COM Port";

        private const string traceStateProfileName = "Trace Level";
        private const string traceStateDefault = "false";

        // Variables to hold the current device configuration
        internal bool autoDetectComPort = Convert.ToBoolean(autoDetectComPortDefault);
        internal string comPortOverride = comPortDefault;

        private const int MAX_BRIGHTNESS = 1;

        // Constants used to communicate with the device
        // Make sure those values are identical to those in the Arduino Firmware.
        // (I could not come up with an easy way to share them across the two projects)
        private const string SEPARATOR = "\n";

        private const string DEVICE_GUID = "55c7745e-d21a-43da-abc5-837adcb27344";

        private const string OK = "OK";

        private const string COMMAND_PING = "COMMAND:PING";
        private const string RESULT_PING = "RESULT:PING:OK:";

        private const string COMMAND_COVER_OPEN = "COMMAND:COVER:OPEN";
        private const string RESULT_COVER_OPEN = "RESULT:COVER:OPEN:";

        private const string COMMAND_COVER_CLOSE = "COMMAND:COVER:CLOSE";
        private const string RESULT_COVER_CLOSE = "RESULT:COVER:CLOSE:";

        private const string COMMAND_COVER_CALIBRATE = "COMMAND:COVER:CALIBRATE";
        private const string RESULT_COVER_CALIBRATE = "RESULT:COVER:CALIBRATE:";

        private const string COMMAND_COVER_GETSTATE = "COMMAND:COVER:GETSTATE";
        private const string RESULT_COVER_GETSTATE = "RESULT:COVER:GETSTATE:";

        private const string COMMAND_CALIBRATOR_ON = "COMMAND:CALIBRATOR:ON";
        private const string RESULT_CALIBRATOR_ON = "RESULT:CALIBRATOR:ON:";

        private const string COMMAND_CALIBRATOR_OFF = "COMMAND:CALIBRATOR:OFF";
        private const string RESULT_CALIBRATOR_OFF = "RESULT:CALIBRATOR:OFF:";

        private const string COMMAND_CALIBRATOR_GETSTATE = "COMMAND:CALIBRATOR:GETSTATE";
        private const string RESULT_CALIBRATOR_GETSTATE = "RESULT:CALIBRATOR:GETSTATE:";

        /// <summary>
        /// Private variable to hold the connected state
        /// </summary>
        private bool connectedState;

        /// <summary>
        // The object used to communicate with the device using serial port communication.
        /// </summary>
        private Serial objSerial;

        /// <summary>
        // Object used to synchronize the serial communication with the device in a multi-threaded environment.
        /// </summary>
        private readonly object lockObject = new object();

        /// <summary>
        /// Variable to hold the trace logger object (creates a diagnostic log file with information that you specify)
        /// </summary>
        internal TraceLogger tl;

        /// <summary>
        /// Initializes a new instance of the <see cref="DarkSkyGeek"/> class.
        /// Must be public for COM registration.
        /// </summary>
        public TelescopeCoverV2()
        {
            tl = new TraceLogger("", "DarkSkyGeek");
            tl.LogMessage("TelescopeCoverV2", "Starting initialization");
            ReadProfile();
            connectedState = false;
            tl.LogMessage("TelescopeCoverV2", "Completed initialization");
        }

        //
        // PUBLIC COM INTERFACE ICoverCalibratorV1 IMPLEMENTATION
        //

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialog form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public void SetupDialog()
        {
            // consider only showing the setup dialog if not connected
            // or call a different dialog if connected
            if (IsConnected)
            {
                MessageBox.Show("Already connected, just press OK");
            }

            using (SetupDialogForm F = new SetupDialogForm(this))
            {
                var result = F.ShowDialog();
                if (result == DialogResult.OK)
                {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        /// <summary>Returns the list of custom action names supported by this driver.</summary>
        /// <value>An ArrayList of strings (SafeArray collection) containing the names of supported actions.</value>
        public ArrayList SupportedActions
        {
            get
            {
                tl.LogMessage("SupportedActions Get", "Returning empty arraylist");
                return new ArrayList();
            }
        }

        /// <summary>Invokes the specified device-specific custom action.</summary>
        /// <param name="ActionName">A well known name agreed by interested parties that represents the action to be carried out.</param>
        /// <param name="ActionParameters">List of required parameters or an <see cref="String.Empty">Empty String</see> if none are required.</param>
        /// <returns>A string response. The meaning of returned strings is set by the driver author.
        /// <para>Suppose filter wheels start to appear with automatic wheel changers; new actions could be <c>QueryWheels</c> and <c>SelectWheel</c>. The former returning a formatted list
        /// of wheel names and the second taking a wheel name and making the change, returning appropriate values to indicate success or failure.</para>
        /// </returns>
        public string Action(string actionName, string actionParameters)
        {
            switch (actionName.ToUpper())
            {
                case "CALIBRATECOVERPOSITIONFEEDBACK":
                    string response = SendCommandToDevice("CalibrateCoverPositionFeedback", COMMAND_COVER_CALIBRATE, RESULT_COVER_CALIBRATE);
                    return response;
                default:
                    LogMessage("", "Action {0} not implemented", actionName);
                    throw new ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");
            }
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and does not wait for a response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        public void CommandBlind(string command, bool raw)
        {
            CheckConnected("CommandBlind");
            throw new ASCOM.MethodNotImplementedException("CommandBlind");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a boolean response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the interpreted boolean response received from the device.
        /// </returns>
        public bool CommandBool(string command, bool raw)
        {
            CheckConnected("CommandBool");
            throw new ASCOM.MethodNotImplementedException("CommandBool");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a string response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the string response received from the device.
        /// </returns>
        public string CommandString(string command, bool raw)
        {
            CheckConnected("CommandString");
            throw new ASCOM.MethodNotImplementedException("CommandString");
        }

        /// <summary>
        /// Dispose the late-bound interface, if needed. Will release it via COM
        /// if it is a COM object, else if native .NET will just dereference it
        /// for GC.
        /// </summary>
        public void Dispose()
        {
            Connected = false;
            tl.Enabled = false;
            tl.Dispose();
            tl = null;
        }

        /// <summary>
        /// Set True to connect to the device hardware. Set False to disconnect from the device hardware.
        /// You can also read the property to check whether it is connected. This reports the current hardware state.
        /// </summary>
        /// <value><c>true</c> if connected to the hardware; otherwise, <c>false</c>.</value>
        public bool Connected
        {
            get
            {
                LogMessage("Connected", "Get {0}", IsConnected);
                return IsConnected;
            }
            set
            {
                LogMessage("Connected", "Set {0}", value);
                if (value == IsConnected)
                    return;

                if (value)
                {
                    LogMessage("Connected Set", "Connecting");

                    Debug.Assert(objSerial == null);

                    using (Profile driverProfile = new Profile() { DeviceType = "CoverCalibrator" })
                    {
                        Serial serial = null;

                        var comPorts = new List<string>(System.IO.Ports.SerialPort.GetPortNames());

                        if (autoDetectComPort)
                        {
                            // See if the last successfully connected COM port can be used first...
                            // This is a performance optimization that significantly reduces the time it takes to connect!
                            string lastComPort = driverProfile.GetValue(driverID, lastComPortProfileName, string.Empty, string.Empty);
                            if (!string.IsNullOrEmpty(lastComPort))
                            {
                                var i = comPorts.IndexOf(lastComPort);
                                if (i >= 0)
                                {
                                    // Move the last successfully connected COM port to the top of the list of available COM ports
                                    // (if it was found in that list to begin with...) so that we try that first.
                                    comPorts.RemoveAt(i);
                                    comPorts.Insert(0, lastComPort);
                                }
                            }

                            foreach (string comPortName in comPorts)
                            {
                                serial = ConnectToDevice(comPortName);
                                if (serial != null)
                                {
                                    break;
                                }
                            }
                        }
                        else if (comPorts.Contains(comPortOverride))
                        {
                            serial = ConnectToDevice(comPortOverride);
                        }
                        else
                        {
                            throw new InvalidValueException("Invalid COM port", comPortOverride, String.Join(", ", System.IO.Ports.SerialPort.GetPortNames()));
                        }

                        if (serial != null)
                        {
                            objSerial = serial;

                            // Persist the COM port name so that we try that first the next time
                            // we attempt to connect (see code above in this method)
                            driverProfile.WriteValue(driverID, lastComPortProfileName, serial.PortName);

                            LogMessage("Connected Set", "Connected to port {0}", serial.PortName);

                            connectedState = true;
                        }
                        else
                        {
                            throw new NotConnectedException("Failed to connect");
                        }
                    }
                }
                else
                {
                    connectedState = false;

                    LogMessage("Connected Set", "Disconnecting");

                    objSerial.Connected = false;
                    objSerial.Dispose();
                    objSerial = null;

                    // Wait for the serial connection to be fully closed...
                    // See https://stackoverflow.com/questions/6434297/why-thread-sleep-before-serialport-open-and-close
                    // TODO: Is there a better way?
                    System.Threading.Thread.Sleep(1000);
                }
            }
        }

        /// <summary>
        /// Returns a description of the device, such as manufacturer and modelnumber. Any ASCII characters may be used.
        /// </summary>
        /// <value>The description.</value>
        public string Description
        {
            get
            {
                tl.LogMessage("Description Get", deviceName);
                return deviceName;
            }
        }

        /// <summary>
        /// Descriptive and version information about this ASCOM driver.
        /// </summary>
        public string DriverInfo
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverInfo = deviceName + " Version " + String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        /// <summary>
        /// A string containing only the major and minor version of the driver formatted as 'm.n'.
        /// </summary>
        public string DriverVersion
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        /// <summary>
        /// The interface version number that this device supports. 
        /// </summary>
        public short InterfaceVersion
        {
            // set by the driver wizard
            get
            {
                LogMessage("InterfaceVersion Get", "1");
                return Convert.ToInt16("1");
            }
        }

        /// <summary>
        /// The short name of the driver, for display purposes.
        /// </summary>
        public string Name
        {
            get
            {
                tl.LogMessage("Name Get", deviceName);
                return deviceName;
            }
        }

        #endregion

        #region ICoverCalibrator Implementation

        /// <summary>
        /// Returns the state of the device cover, if present, otherwise returns "NotPresent"
        /// </summary>
        public CoverStatus CoverState
        {
            get
            {
                string response = SendCommandToDevice("CoverState", COMMAND_COVER_GETSTATE, RESULT_COVER_GETSTATE);

                switch (response)
                {
                    case "OPEN":
                        return CoverStatus.Open;
                    case "CLOSED":
                        return CoverStatus.Closed;
                    case "OPENING":
                    case "CLOSING":
                        return CoverStatus.Moving;
                    default:
                        return CoverStatus.Unknown;
                }
            }
        }

        /// <summary>
        /// Initiates cover opening if a cover is present
        /// </summary>
        public void OpenCover()
        {
            string response = SendCommandToDevice("CloseCover", COMMAND_COVER_OPEN, RESULT_COVER_OPEN);
            if (response != OK)
            {
                throw new DriverException("Invalid response from device: " + response + " - Device may be currently moving, or may not have been calibrated.");
            }
        }

        /// <summary>
        /// Initiates cover closing if a cover is present
        /// </summary>
        public void CloseCover()
        {
            string response = SendCommandToDevice("CloseCover", COMMAND_COVER_CLOSE, RESULT_COVER_CLOSE);
            if (response != OK)
            {
                throw new DriverException("Invalid response from device: " + response + " - Device may be currently moving, or may not have been calibrated.");
            }
        }

        /// <summary>
        /// Stops any cover movement that may be in progress if a cover is present and cover movement can be interrupted.
        /// </summary>
        public void HaltCover()
        {
            tl.LogMessage("HaltCover", "Not implemented");
            throw new MethodNotImplementedException("HaltCover");
        }

        /// <summary>
        /// Returns the state of the calibration device, if present, otherwise returns CalibratorStatus.NotPresent
        /// </summary>
        public CalibratorStatus CalibratorState
        {
            get
            {
                return CalibratorStatus.Ready;
            }
        }

        /// <summary>
        /// Returns the current calibrator brightness in the range 0 (completely off) to <see cref="MaxBrightness"/> (fully on)
        /// </summary>
        public int Brightness
        {
            get
            {
                string response = SendCommandToDevice("Brightness", COMMAND_CALIBRATOR_GETSTATE, RESULT_CALIBRATOR_GETSTATE);

                if (response == "ON")
                {
                    return MAX_BRIGHTNESS;
                }

                return 0;
            }
        }

        /// <summary>
        /// The Brightness value that makes the calibrator deliver its maximum illumination.
        /// </summary>
        public int MaxBrightness
        {
            get
            {
                return MAX_BRIGHTNESS;
            }
        }

        /// <summary>
        /// Turns the calibrator on at the specified brightness if the device has calibration capability
        /// </summary>
        /// <param name="Brightness"></param>
        public void CalibratorOn(int Brightness)
        {
            if (Brightness < 0 || Brightness > MAX_BRIGHTNESS)
            {
                throw new InvalidValueException("Invalid brightness value", Brightness.ToString(), "[0, " + MAX_BRIGHTNESS.ToString() + "]");
            }

            if (Brightness == 0)
            {
                CalibratorOff();
                return;
            }

            string response = SendCommandToDevice("CalibratorOn", COMMAND_CALIBRATOR_ON, RESULT_CALIBRATOR_ON);
            if (response != OK)
            {
                throw new DriverException("Invalid response from device: " + response);
            }
        }

        /// <summary>
        /// Turns the calibrator off if the device has calibration capability
        /// </summary>
        public void CalibratorOff()
        {
            string response = SendCommandToDevice("CalibratorOn", COMMAND_CALIBRATOR_OFF, RESULT_CALIBRATOR_OFF);
            if (response != OK)
            {
                throw new DriverException("Invalid response from device: " + response);
            }
        }

        #endregion

        #region Private properties and methods

        // Here are some useful properties and methods that can be used as required
        // to help with driver development...

        #region ASCOM Registration

        // Register or unregister driver for ASCOM. This is harmless if already
        // registered or unregistered. 
        //
        /// <summary>
        /// Register or unregister the driver with the ASCOM Platform.
        /// This is harmless if the driver is already registered/unregistered.
        /// </summary>
        /// <param name="bRegister">If <c>true</c>, registers the driver, otherwise unregisters it.</param>
        private static void RegUnregASCOM(bool bRegister)
        {
            using (var P = new Profile())
            {
                P.DeviceType = "CoverCalibrator";
                if (bRegister)
                {
                    P.Register(driverID, deviceName);
                }
                else
                {
                    P.Unregister(driverID);
                }
            }
        }

        /// <summary>
        /// This function registers the driver with the ASCOM Chooser and
        /// is called automatically whenever this class is registered for COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is successfully built.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During setup, when the installer registers the assembly for COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually register a driver with ASCOM.
        /// </remarks>
        [ComRegisterFunction]
        public static void RegisterASCOM(Type t)
        {
            RegUnregASCOM(true);
        }

        /// <summary>
        /// This function unregisters the driver from the ASCOM Chooser and
        /// is called automatically whenever this class is unregistered from COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is cleaned or prior to rebuilding.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During uninstall, when the installer unregisters the assembly from COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually unregister a driver from ASCOM.
        /// </remarks>
        [ComUnregisterFunction]
        public static void UnregisterASCOM(Type t)
        {
            RegUnregASCOM(false);
        }

        #endregion

        /// <summary>
        /// Returns true if there is a valid connection to the device.
        /// </summary>
        private bool IsConnected
        {
            get
            {
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "CoverCalibrator";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(driverID, traceStateProfileName, string.Empty, traceStateDefault));
                autoDetectComPort = Convert.ToBoolean(driverProfile.GetValue(driverID, autoDetectComPortProfileName, string.Empty, autoDetectComPortDefault));
                comPortOverride = driverProfile.GetValue(driverID, comPortProfileName, string.Empty, comPortDefault);
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "CoverCalibrator";
                driverProfile.WriteValue(driverID, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(driverID, autoDetectComPortProfileName, autoDetectComPort.ToString());
                if (comPortOverride != null)
                {
                    driverProfile.WriteValue(driverID, comPortProfileName, comPortOverride.ToString());
                }
            }
        }

        /// <summary>
        /// Attempts to connect to the specified COM port.
        /// Returns a Serial object if successful, null otherwise.
        /// </summary>
        /// <param name="comPortName"></param>
        private Serial ConnectToDevice(string comPortName)
        {
            if (!System.IO.Ports.SerialPort.GetPortNames().Contains(comPortName))
            {
                throw new InvalidValueException("Invalid COM port", comPortName, String.Join(", ", System.IO.Ports.SerialPort.GetPortNames()));
            }

            Serial serial;

            LogMessage("ConnectToDevice", "Connecting to port {0}", comPortName);

            try
            {
                serial = new Serial
                {
                    Speed = SerialSpeed.ps57600,
                    PortName = comPortName,
                    Connected = true,
                    // With this device, we can use a short timeout value.
                    ReceiveTimeout = 1
                };
            }
            catch (Exception)
            {
                // If trying to connect to a port that is already in use, an exception will be thrown.
                return null;
            }

            // Wait for the serial connection to establish...
            // TODO: Is there a better way?
            System.Threading.Thread.Sleep(1000);

            serial.ClearBuffers();

            // Poll the device (with the short timeout value set above) until successful,
            // or until we've reached the retry count limit of 3...
            for (int retries = 3; retries >= 0; retries--)
            {
                string response = "";

                lock (lockObject)
                {
                    try
                    {
                        serial.Transmit(COMMAND_PING + SEPARATOR);
                        response = serial.ReceiveTerminated(SEPARATOR).Trim();
                    }
                    catch (Exception)
                    {
                        // PortInUse or Timeout exceptions may happen here!
                        // We ignore them.
                    }
                }

                if (response == RESULT_PING + DEVICE_GUID)
                {
                    return serial;
                }
            }

            serial.Connected = false;
            serial.Dispose();

            return null;
        }

        /// <summary>
        /// Send a command to the device and returns the response
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="command"></param>
        /// <param name="resultPrefix"></param>
        private string SendCommandToDevice(string identifier, string command, string resultPrefix)
        {
            CheckConnected(identifier);

            LogMessage(identifier, "Sending command " + command + " to device...");

            string response;

            lock (lockObject)
            {
                objSerial.Transmit(command + SEPARATOR);

                LogMessage(identifier, "Waiting for response from device...");

                try
                {
                    response = objSerial.ReceiveTerminated(SEPARATOR).Trim();
                }
                catch (Exception e)
                {
                    LogMessage(identifier, "Exception: " + e.Message);
                    throw e;
                }
            }
            
            LogMessage(identifier, "Response from device: " + response);

            if (!response.StartsWith(resultPrefix))
            {
                LogMessage(identifier, "Invalid response from device: " + response);
                throw new DriverException("Invalid response from device: " + response);
            }

            string arg = response.Substring(resultPrefix.Length);
            return arg;
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            tl.LogMessage(identifier, msg);
        }

        #endregion
    }
}
