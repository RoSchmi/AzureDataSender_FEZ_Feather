


// Application: 'AzureDataSender_FEZ_Feather'
// Copyright RoSchmi 2020, Date 2021 Apr 11 License Apache 2.0
// This App runs on a GHI FEZ Feather Board, for other GHI boards the GPIO Pins have to be adapted
// This App shows how to write Sensor-Data (4 analog Sensors) and data from up to 4 On/OffSensors to Azure Storage Tables
//
// If the directive 'UseTestValues' is activated, automatically created 'SensorValues' are uploaded every 150 sec (can be changed in 'writeToCloudInterval') which will form sinus curves
// when viewed with the App Charts4Azure (iOS, Android, Microsoft UWP). 
// The Microsoft UWP Version of Charts4Azure is free of charge.
//
// The directive 'UseWifiModule' determines whether WiFi or an Enc28 Module is used (Enc28 not yet realized)
// 
// When the Button 'App' on the board is pressed (hold it some 10 seconds) and released this results in a changed state of the On/Off-Graph in 'Charts4Azure' 
// 
// First go to 'Settings to be changed by user' and enter your credentials for your Azure Storage Account and your WiFi Credentials
// 
// The Credentials can either be entered directly in the Code or can better be in  .txt files in the 'ResourcesSecret' folder
//
// With #define UseTestValues you can select if data are read from sensors or if simulated data (sinus curves) are used



#define UseTestValues

// With #define UseWiFi you can select if the WiFi7 Click Module on microBus1 or an Enc28 Ethernet Module is used

#define UseWifiModule

using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.Adc;
using GHIElectronics.TinyCLR.Devices.Rtc;
using GHIElectronics.TinyCLR.Devices.Spi;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Network;
using GHIElectronics.TinyCLR.Native;

//using GHIElectronics.TinyCLR.Native.
using RoSchmi.TinyCLR2.Time;
using RoSchmi.Net.Azure.Storage;
using AzureDataSender;


namespace AzureDataSender_FEZ_Feather
{
    class Program
    {

        #region Region Settings to be changed by user
        //****************  Settings to be changed by user   ********************************* 

        private static AzureStorageHelper.DebugMode _debug = AzureStorageHelper.DebugMode.StandardDebug;
        private static AzureStorageHelper.DebugLevel _debug_level = AzureStorageHelper.DebugLevel.DebugAll;

        private const string TimeServer_1 = "time1.google.com";
        private const string TimeServer_2 = "1.pool.ntp.org";

        private static int timeZoneOffset = 120;         // Berlin offest in minutes of your timezone to Greenwich Mean Time (GMT)
                                                        //private static int timeZoneOffset = 120;      // Berlin (DaylightsavingTime) offest in minutes of your timezone to Greenwich Mean Time (GMT)

        // Set the name of the table for analog values (name must be conform to special rules: see Azure)
        private static string analogTableName = "AnalogValuesX";

        private static string analogTablePartPrefix = "Y2_";     // Your choice (name must be conform to special rules: see Azure)
        private static bool augmentPartitionKey = true;          // If true, the actual year is added as suffix to the Tablenames

        // Set the name of the table for On/Off values (name must be conform to special rules: see Azure)
        //private static string OnOffSensor01TableName = "OnOffValues01";
        private static string OnOffSensor01TableName = "OnOffx01x";

        static string onOffTablePartPrefix = "Y3_";             // Your choice (name must be conform to special rules: see Azure)

        // Set intervals (in seconds, invalidateInterval in minutes)
        static int readInterval = 4;                     // in this interval (seconds) analog sensors are read

        static int writeToCloudInterval = 300;   // for tests 30, in this interval(seconds) the analog data are stored to the cloud

        //static int writeToCloudInterval = 150;  // for real application 150 or more, in this interval (seconds) the analog data are stored to the cloud 

        static int invalidateIntervalMinutes = 15;      // if analog values ar not actualized in this interval, they are set to invalid (999.9)


        // Set your WiFi Credentials here or store them in the Resources
        static string wiFiSSID_1 = ResourcesSecret.GetString(ResourcesSecret.StringResources.SSID_1);
        //static string wiFiSSID_1 = "VirtualWiFi";

        static string wiFiKey_1 = ResourcesSecret.GetString(ResourcesSecret.StringResources.Key_1);
        //static string wiFiKey_1 = "MySecretWiFiKey";

        // Set your Azure Storage Account Credentials here or store them in the Resources      
        static string storageAccountName = ResourcesSecret.GetString(ResourcesSecret.StringResources.AzureAccountName);
        //static string storageAccount = "your Accountname";

        static string storageKey = ResourcesSecret.GetString(ResourcesSecret.StringResources.AzureAccountKey);
        //static string storageKey = "your key";

        private static bool Azure_useHTTPS = true;
        //private static bool Azure_useHTTPS = false;


        //****************  End of Settings to be changed by user   ********************************* 

        #endregion

        #region Region Fields and Declarations

        private static bool linkReady = false;

        private static bool timeServiceIsRunning = false;

        private static bool timeIsSet = false;

        private static int uploadCounter = 0;

        private static NetworkController networkController;

        private static X509Certificate[] caCerts = Azure_useHTTPS ? new X509Certificate[] { new X509Certificate(Resources.GetBytes(Resources.BinaryResources.BaltimoreCyberTrustRoot)) } : null;

        private static int AnalogCloudTableYear = 1900;   // preset with year in the past

        private static TableClient table;

        // Create Datacontainer for values of 4 analog channels, Data invalidate time = 15 min
        private static DataContainer dataContainer = new DataContainer(new TimeSpan(0, invalidateIntervalMinutes, 0));

        private static Timer getSensorDataTimer;
        private static Timer writeAnalogToCloudTimer;
        private static Timer readLastAnalogRowTimer;

        private static readonly object LockProgram = new object();

        private static GpioPin OnOffSensor01;

        private static CloudStorageAccount myCloudStorageAccount;

        // ADC Controller pins still have to be adapted to SC20100 board
        private static AdcController adc = AdcController.FromName(SC20260.Adc.Controller1.Id);

        // ADC Controller pins still have to be adapted to SC20100 board
        private static AdcChannel analog0 = adc.OpenChannel(SC20260.Adc.Controller1.PA0);
        private static AdcChannel analog1 = adc.OpenChannel(SC20260.Adc.Controller1.PA1C);
        private static AdcChannel analog2 = adc.OpenChannel(SC20260.Adc.Controller3.PC2C);
        private static AdcChannel analog3 = adc.OpenChannel(SC20260.Adc.Controller3.PC3C);

        // For OnOffSensor01, must be created for OnOffSensor02 - OnOffSensor04 if needed       
        static DateTime OnOffSensor01LastSendTime = DateTime.MinValue;
        static TimeSpan OnOffSensor01OnTimeDay = new TimeSpan(0, 0, 0);
        private static int OnOffTable01Year = 1900;
        #endregion

        #region Region Main()
        static void Main()
        {
            // Use low cpu frequency
            //https://docs.ghielectronics.com/software/tinyclr/tutorials/power-management.html

            // Cave: Do not activate both code alternatives together to change System Clock
            // If the board gets unresponsive erase application in loader mode (ldr button)
            // 

            var PersistClock = true;
            if (Power.GetSystemClock() == SystemClock.High)
            {
                Power.SetSystemClock(SystemClock.Low, PersistClock);
                Power.Reset();
            }

            /*
            if (Power.GetSystemClock() == SystemClock.Low)
            {
                Power.SetSystemClock(SystemClock.High, false);
                Power.Reset();
            }
            */

            // Print System.Clock state
            Debug.WriteLine(Power.GetSystemClock() == SystemClock.Low ? "Using low cpu-frequency" : "Using high cpu-frequency");

           
            var LED = GpioController.GetDefault().OpenPin(SC20100.GpioPin.PE11);
            LED.SetDriveMode(GpioPinDriveMode.Output);
            // Signals start of program (for tests)
            
            for (int i = 0; i < 5; i++)
            {
                LED.Write(GpioPinValue.High);
                Thread.Sleep(600);

                LED.Write(GpioPinValue.Low);
                Thread.Sleep(600);
            }
            

            myCloudStorageAccount = myCloudStorageAccount = new CloudStorageAccount(storageAccountName, storageKey, useHttps: Azure_useHTTPS);

            //SetupEnc28_SC20260D_MicroBus1();
#if UseWifiModule
            SetupWiFi7Click_SC20100_MicroBus1();

            //Print the version of the installed WiFi firmware:        
            Debug.WriteLine("Winc1500 Firmware Version: " + GHIElectronics.TinyCLR.Drivers.Microchip.Winc15x0.Winc15x0Interface.GetFirmwareVersion());

            Debug.WriteLine("Supported Firmware Versions are: ");
            for (int i = 0; i < GHIElectronics.TinyCLR.Drivers.Microchip.Winc15x0.Winc15x0Interface.FirmwareSupports.Length; i++)
            {
                System.Diagnostics.Debug.WriteLine("Supported firmware version #" +
                    (i + 1).ToString() + ": " + GHIElectronics.TinyCLR.Drivers.Microchip.Winc15x0.Winc15x0Interface.FirmwareSupports[i].ToString());
            }

#else
            SetupEnc28_SC20260D_MicroBus1();
#endif



            TimeService.SystemTimeChanged += TimeService_SystemTimeChanged;
            TimeService.SystemTimeChecked += TimeService_SystemTimeChecked;

            SetAppTime(timeZoneOffset, TimeServer_1, TimeServer_2);

            // Settings for one OnOffSensor, must be created for OnOffSensor02 - OnOffSensor04 if needed 
            OnOffSensor01 = GpioController.GetDefault().OpenPin(SC20260.GpioPin.PB7);
            OnOffSensor01.SetDriveMode(GpioPinDriveMode.InputPullUp);
            OnOffSensor01.ValueChanged += OnOffSensor01_ValueChanged;

            getSensorDataTimer = new System.Threading.Timer(new TimerCallback(getSensorDataTimer_tick), null, readInterval * 1000, readInterval * 1000);

            // start timer to write analog data to the Cloud
            writeAnalogToCloudTimer = new System.Threading.Timer(new TimerCallback(writeAnalogToCloudTimer_tick), null, 5 * 1000, Timeout.Infinite);

            // readLastAnalogRowTimer is started in writeAnalogToCloudTimer_tick event
            readLastAnalogRowTimer = new System.Threading.Timer(new TimerCallback(readLastAnalogRowTimer_tick), null, Timeout.Infinite, Timeout.Infinite);


            Thread.Sleep(-1);
        }
        #endregion

        #region Timer Event: writeAnalogToCloudTimer_tick  --- Entity with analog values is written to the Cloud
        private static void writeAnalogToCloudTimer_tick(object state)
        {
            writeAnalogToCloudTimer.Change(10 * 60 * 1000, 10 * 60 * 1000);    // Set to a long interval, so will not fire again before completed

            lock (LockProgram)
            {
                int yearOfSend = DateTime.Now.Year;

                #region Region Create analogTable if not exists
                HttpStatusCode resultTableCreate = HttpStatusCode.Ambiguous;
                if (AnalogCloudTableYear != yearOfSend)
                {
                    Debug.WriteLine("\r\nGoing to create analog Table");
                    resultTableCreate = createTable(myCloudStorageAccount, caCerts, analogTableName + DateTime.Today.Year.ToString());
                }

                #endregion

                if ((resultTableCreate == HttpStatusCode.Created) || (resultTableCreate == HttpStatusCode.NoContent) || (resultTableCreate == HttpStatusCode.Conflict))
                {
                    // Set flag to indicate that table already exists, avoid trying to create again
                    AnalogCloudTableYear = yearOfSend;

                    //writeAnalogToCloudTimer.Change(1 * 1000, writeToCloudInterval * 1000);  // set the timer event to come again in 1 sec.
                    writeAnalogToCloudTimer.Change(4000, 4000);  // set the timer event to come again in 4 sec.                                     
                }
                else
                {
                    string partitionKey = makePartitionKey(analogTablePartPrefix, augmentPartitionKey);

                    DateTime actDate = DateTime.Now;

                    string rowKey = makeRowKey(actDate);

                    string TimeOffsetUTCString = timeZoneOffset < 0 ? timeZoneOffset.ToString("D3") : "+" + timeZoneOffset.ToString("D3");
                    string sampleTime = actDate.Month.ToString("D2") + "/" + actDate.Day.ToString("D2") + "/" + actDate.Year + " " + actDate.Hour.ToString("D2") + ":" + actDate.Minute.ToString("D2") + ":" + actDate.Second.ToString("D2") + " " + TimeOffsetUTCString;


                    // Fill array with 4 analog values from datacontainer
                    double[] sampleValues = new double[4];
                    for (int i = 1; i < 5; i++)
                    {
                        double measuredValue = dataContainer.GetAnalogValueSet(i).MeasureValue;
                        // limit measured values to the allowed range of -40.0 to +140.0, exception: 999.9 (not valid value)
                        if ((measuredValue < 999.89) || (measuredValue > 999.91))  // want to be careful with decimal numbers
                        {
                            measuredValue = (measuredValue < -40.0) ? -40.0 : (measuredValue > 140.0 ? 140.0 : measuredValue);
                        }
                        else
                        {
                            measuredValue = 999.9;
                        }
                        sampleValues[i - 1] = measuredValue;
                    }



                    // Populate Analog Table with values from the array
                    ArrayList propertiesAL = AnalogTablePropertiesAL.AnalogPropertiesAL(sampleTime, sampleValues[0], sampleValues[1], sampleValues[2], sampleValues[3]);

                    AnalogTableEntity analogTableEntity = new AnalogTableEntity(partitionKey, rowKey, propertiesAL);

                    Debug.WriteLine("\r\nGoing to upload analog values.     SampleTime: " + sampleTime);

                    string insertEtag = string.Empty;
                    HttpStatusCode insertResult = HttpStatusCode.BadRequest;

                    insertResult = insertTableEntity(myCloudStorageAccount, caCerts, analogTableName + yearOfSend.ToString(), analogTableEntity, out insertEtag);

                    uploadCounter++;

                    if ((insertResult == HttpStatusCode.NoContent) || (insertResult == HttpStatusCode.Conflict))
                    {
                        Debug.WriteLine("Succeeded to insert Entity\r\n");

                        writeAnalogToCloudTimer.Change(writeToCloudInterval * 1000, writeToCloudInterval * 1000);

                        // trigger the timer to read the last row
                        readLastAnalogRowTimer.Change(1000, Timeout.Infinite);
                    }
                    else
                    {
                        Debug.WriteLine("Failed to insert Entity\r\n");
                        writeAnalogToCloudTimer.Change(1000, writeToCloudInterval * 1000);
                    }
                }
            }
        }
        #endregion

        #region Timer Event: readLastAnalogRowTimer_tick
        private static void readLastAnalogRowTimer_tick(object state)
        {
            lock (LockProgram)
            {
                Debug.WriteLine("Going to read back last uploaded entity");

                ArrayList queryResult = new ArrayList();

                HttpStatusCode resultQuery = queryTableEntities(myCloudStorageAccount, caCerts, analogTableName + DateTime.Now.Year.ToString(), "$top=1", out queryResult);

                if (resultQuery == HttpStatusCode.OK)
                {
                    var entityHashtable = queryResult[0] as Hashtable;
                    var theRowKey = entityHashtable["RowKey"];
                    var SampleTime = entityHashtable["SampleTime"];
                    Debug.WriteLine("Successfully read back from Azure, SampleTime: " + SampleTime);
                }
                else
                {
                    Debug.WriteLine("Failed to read back last entity from Azure");
                }
                // the timer is set to a short time in 'writeAnalogToCloudTimer_tick'
                readLastAnalogRowTimer.Change(Timeout.Infinite, Timeout.Infinite);

                long freeMemory = GHIElectronics.TinyCLR.Native.Memory.ManagedMemory.FreeBytes;
                long totalMemory = GC.GetTotalMemory(true);
                Debug.WriteLine("Total Memory: " + totalMemory.ToString("N0") + " Free Bytes: " + freeMemory.ToString("N0"));
            }
        }
        #endregion

        #region Timer Event: getSensorDataTimer_tick
        private static void getSensorDataTimer_tick(object state)
        {
            lock (LockProgram)
            {
                DateTime actDateTime = DateTime.Now;

                dataContainer.SetNewAnalogValue(1, actDateTime, ReadAnalogSensor(0));
                dataContainer.SetNewAnalogValue(2, actDateTime, ReadAnalogSensor(1));
                dataContainer.SetNewAnalogValue(3, actDateTime, ReadAnalogSensor(2));
                dataContainer.SetNewAnalogValue(4, actDateTime, ReadAnalogSensor(3));
            }
        }
        #endregion

        #region Timer Event: OnOffSensor01_ValueChanged  -- Entity with OnOffSensorData is written to the Cloud

        private static void OnOffSensor01_ValueChanged(GpioPin sender, GpioPinValueChangedEventArgs e)
        {
            lock (LockProgram)
            {
                int yearOfSend = DateTime.Now.Year;

                // Create OnOffTable if not exists
                HttpStatusCode resultTableCreate = HttpStatusCode.Ambiguous;
                if (OnOffTable01Year != yearOfSend)
                {
                    Debug.WriteLine("Going to create On/Off Table");

                    resultTableCreate = createTable(myCloudStorageAccount, caCerts, OnOffSensor01TableName + DateTime.Today.Year.ToString());
                }

                if ((resultTableCreate == HttpStatusCode.Created) || (resultTableCreate == HttpStatusCode.NoContent) || (resultTableCreate == HttpStatusCode.Conflict))
                {
                    OnOffTable01Year = yearOfSend;
                }
                else
                {
                    string partitionKey = makePartitionKey(onOffTablePartPrefix, augmentPartitionKey);

                    DateTime actDate = DateTime.Now;
                    string rowKey = makeRowKey(actDate);

                    string TimeOffsetUTCString = timeZoneOffset < 0 ? timeZoneOffset.ToString("D3") : "+" + timeZoneOffset.ToString("D3");

                    string sampleTime = actDate.Month.ToString("D2") + "/" + actDate.Day.ToString("D2") + "/" + actDate.Year + " " + actDate.Hour.ToString("D2") + ":" + actDate.Minute.ToString("D2") + ":" + actDate.Second.ToString("D2") + " " + TimeOffsetUTCString;

                    TimeSpan tflSend = OnOffSensor01LastSendTime == DateTime.MinValue ? new TimeSpan(0) : e.Timestamp - OnOffSensor01LastSendTime;

                    OnOffSensor01LastSendTime = e.Timestamp;

                    string timeFromLastSendAsString = tflSend.Days.ToString("D3") + "-" + tflSend.Hours.ToString("D2") + ":" + tflSend.Minutes.ToString("D2") + ":" + tflSend.Seconds.ToString("D2");

                    OnOffSensor01OnTimeDay = OnOffSensor01.Read() == GpioPinValue.High ? OnOffSensor01OnTimeDay + tflSend : OnOffSensor01OnTimeDay;

                    string onTimeDayAsString = OnOffSensor01OnTimeDay.Days.ToString("D3") + "-" + OnOffSensor01OnTimeDay.Hours.ToString("D2") + ":" + OnOffSensor01OnTimeDay.Minutes.ToString("D2") + ":" + OnOffSensor01OnTimeDay.Seconds.ToString("D2");

                    ArrayList propertiesAL = OnOffTablePropertiesAL.OnOffPropertiesAL(OnOffSensor01.Read() == GpioPinValue.Low ? "On" : "Off", OnOffSensor01.Read() == GpioPinValue.Low ? "Off" : "On", onTimeDayAsString, sampleTime, timeFromLastSendAsString);

                    OnOffTableEntity onOffTableEntity = new OnOffTableEntity(partitionKey, rowKey, propertiesAL);

                    HttpStatusCode insertResult = HttpStatusCode.BadRequest;

                    while (!((insertResult == HttpStatusCode.NoContent) || (insertResult == HttpStatusCode.Conflict)))
                    {
                        string insertEtag = string.Empty;
                        string state = OnOffSensor01.Read() == GpioPinValue.Low ? "On" : "Off";
                        Debug.WriteLine("Going to upload OnOff-Sensor State:" + state);
                        insertResult = insertTableEntity(myCloudStorageAccount, caCerts, OnOffSensor01TableName + yearOfSend.ToString(), onOffTableEntity, out insertEtag);

                        Debug.WriteLine(((insertResult == HttpStatusCode.NoContent) || (insertResult == HttpStatusCode.Conflict)) ? "Succeded to insert Entity\r\n" : "Failed to insert Entity *************\r\n");
                    }
                }
            }
        }


        #endregion

        #region event TimeService_SystemTimeChecked
        private static void TimeService_SystemTimeChecked(object sender, SystemTimeChangedEventArgs e)
        {
            Debug.WriteLine("SystemTime was checked! " + DateTime.Now);
        }
        #endregion

        #region event TimeService_SystemTimeChanged
        private static void TimeService_SystemTimeChanged(object sender, SystemTimeChangedEventArgs e)
        {
            Debug.WriteLine("SystemTime has changed. Actual local Time is " + DateTime.Now);
            Debug.WriteLine("Actual utc Time is " + DateTime.UtcNow);
        }
        #endregion

        #region event NetworkController_NetworkLinkConnectedChanged
        private static void NetworkController_NetworkLinkConnectedChanged
            (NetworkController sender, NetworkLinkConnectedChangedEventArgs e)
        {
            // Raise event connect/disconnect
        }
        #endregion

        #region event NetworkController_NetworkAddressChanged
        private static void NetworkController_NetworkAddressChanged
            (NetworkController sender, NetworkAddressChangedEventArgs e)
        {
            var ipProperties = sender.GetIPProperties();
            var address = ipProperties.Address.GetAddressBytes();

            linkReady = address[0] != 0;
        }
        #endregion

        #region private method ReadAnalogSensors      
        private static double ReadAnalogSensor(int pAin)
        {

#if !UseTestValues
            // Use values read from the analogInput ports

            double theRead = 999.9;
            switch (pAin)
            {
                case 0:
                    {
                        theRead = analog0.ReadRatio();
                    }
                    break;

                case 1:
                    {
                        theRead = analog1.ReadRatio();
                    }
                    break;
                case 2:
                    {
                        theRead = analog2.ReadRatio();
                    }
                    break;
                case 3:
                    {
                        theRead = analog3.ReadRatio();
                    }
                    break;
            }

            return theRead * 10.0;

#else
            // Only as an example we here return values which draw a sinus curve
            // Console.WriteLine("entering Read analog sensor");
            int frequDeterminer = 4;
            int y_offset = 1;
            // different frequency and y_offset for aIn_0 to aIn_3
            if (pAin == 0)
            { frequDeterminer = 4; y_offset = 1; }
            if (pAin == 1)
            { frequDeterminer = 8; y_offset = 10; }
            if (pAin == 2)
            { frequDeterminer = 12; y_offset = 20; }
            if (pAin == 3)
            { frequDeterminer = 16; y_offset = 30; }


            int secondsOnDayElapsed = DateTime.Now.Second + DateTime.Now.Minute * 60 + DateTime.Now.Hour * 60 * 60;

            if (pAin == 3)
            {
                return Math.Round((double)(uploadCounter % 100)) / 10;
            }
            else
            {
                return Math.Round(25f * (double)Math.Sin(Math.PI / 2.0 + (secondsOnDayElapsed * ((frequDeterminer * Math.PI) / (double)86400)))) / 10 + y_offset;
            }
#endif
        }
        #endregion

        #region private method insertTableEntity
        private static HttpStatusCode insertTableEntity(CloudStorageAccount pCloudStorageAccount, X509Certificate[] pCaCerts, string pTable, TableEntity pTableEntity, out string pInsertETag)
        {
            table = new TableClient(pCloudStorageAccount, pCaCerts, _debug, _debug_level);
            // To use Fiddler as WebProxy include the following line. Use the local IP-Address of the PC where Fiddler is running
            // see: -http://blog.devmobile.co.nz/2013/01/09/netmf-http-debugging-with-fiddler

            //if (attachFiddler)
            //{ table.attachFiddler(true, fiddlerIPAddress, fiddlerPort); }

            var resultCode = table.InsertTableEntity(pTable, pTableEntity, TableClient.ContType.applicationIatomIxml, TableClient.AcceptType.applicationIjson, TableClient.ResponseType.dont_returnContent, useSharedKeyLite: false);
            pInsertETag = table.OperationResponseETag;
            //var body = table.OperationResponseBody;
            //Debug.Print("Entity inserted");
            return resultCode;
        }
        #endregion

        #region private method createTable
        private static HttpStatusCode createTable(CloudStorageAccount pCloudStorageAccount, X509Certificate[] pCaCerts, string pTableName)
        {
            table = new TableClient(pCloudStorageAccount, pCaCerts, _debug, _debug_level);

            // To use Fiddler as WebProxy include the following line. Use the local IP-Address of the PC where Fiddler is running
            // see: -http://blog.devmobile.co.nz/2013/01/09/netmf-http-debugging-with-fiddler
            /*
            if (attachFiddler)
            { table.attachFiddler(true, fiddlerIPAddress, fiddlerPort); }
            */

            HttpStatusCode resultCode = table.CreateTable(pTableName, TableClient.ContType.applicationIatomIxml, TableClient.AcceptType.applicationIjson, TableClient.ResponseType.dont_returnContent, useSharedKeyLite: false);
            return resultCode;
        }
        #endregion

        #region private method queryTableEntities
        private static HttpStatusCode queryTableEntities(CloudStorageAccount pCloudStorageAccount, X509Certificate[] pCaCerts, string tableName, string query, out ArrayList queryResult)
        {
            //table = new TableClient(pCloudStorageAccount, pCaCerts, _debug, _debug_level, wifi);
            table = new TableClient(pCloudStorageAccount, pCaCerts, _debug, _debug_level);


            // To use Fiddler as WebProxy include the  following line. Use the local IP-Address of the PC where Fiddler is running
            // see: -http://blog.devmobile.co.nz/2013/01/09/netmf-http-debugging-with-fiddler
            //if (attachFiddler)
            //{ table.attachFiddler(true, fiddlerIPAddress, fiddlerPort); }

            HttpStatusCode resultCode = table.QueryTableEntities(tableName, query, TableClient.ContType.applicationIatomIxml, TableClient.AcceptType.applicationIatomIxml, useSharedKeyLite: false);

            // now we can get the results by reading the properties: table.OperationResponse......
            queryResult = table.OperationResponseQueryList;
            // var body = table.OperationResponseBody;
            // this shows how to get a special value (here the RowKey)of the first entity
            // var entityHashtable = queryResult[0] as Hashtable;
            // var theRowKey = entityHashtable["RowKey"];
            return resultCode;
        }

        #endregion

        #region method SetAppTime
        public static void SetAppTime(int pTimeZoneOffset, string pTimeServer_1, string pTimeServer_2)
        {
            // Set parameters of the TimeService
            TimeServiceSettings timeSettings = new TimeServiceSettings()
            {
                //RefreshTime = 60,                          // every 60 sec (for tests)      
                RefreshTime = 2 * 60 * 60,                   // every 2 hours (2 x 60 x 60) default: 300000 sec               
                AutoDayLightSavings = false,                 // We use our own timeshift calculation
                ForceSyncAtWakeUp = true,
                Tolerance = 10000                            // deviation may be up to 10 sec
            };


            int loopCounter = 1;
            while (loopCounter < 3)
            {

                IPAddress[] address = null;
                IPAddress[] address_2 = null;

                try
                {
                    address = System.Net.Dns.GetHostEntry(pTimeServer_1).AddressList;
                }
                catch { };

                try
                {
                    address_2 = System.Net.Dns.GetHostEntry(pTimeServer_2).AddressList;
                }
                catch { };


                try
                {
                    timeSettings.PrimaryServer = address[0].GetAddressBytes();
                }
                catch { };

                try
                {
                    timeSettings.AlternateServer = address_2[0].GetAddressBytes();
                }
                catch { };

                TimeService.Settings = timeSettings;

                TimeService.SetTimeZoneOffset(pTimeZoneOffset);

                Debug.WriteLine("Starting Timeservice");
                TimeService.Start();
                Debug.WriteLine("Returned from Starting Timeservice");
                Thread.Sleep(100);
                if (DateTime.Now > new DateTime(2018, 7, 1))
                {
                    timeServiceIsRunning = true;
                    Debug.WriteLine("Timeserver intialized on try: " + loopCounter);
                    Debug.WriteLine("Synchronization Interval = " + timeSettings.RefreshTime);
                    break;
                }
                else
                {
                    timeServiceIsRunning = false;
                    Debug.WriteLine("Timeserver could not be intialized on try: " + loopCounter);
                }
                loopCounter++;
            }

            var rtc = RtcController.GetDefault();
            if (timeServiceIsRunning)
            {
                rtc.Now = DateTime.UtcNow;
            }
            else
            {
                Debug.WriteLine("No success to get time over internet");
                // Get time from Rtc
                if (rtc.IsValid)
                {
                    SystemTime.SetTime(rtc.Now, timeZoneOffset);
                }
            }

            // SystemTime.SetTime(new DateTime(2000, 1, 1, 1, 1, 1));  //For tests, to see what happens when wrong date

            if (DateTime.Now < new DateTime(2016, 7, 1))
            {
                timeIsSet = false;

                Debug.WriteLine("Restarting Program");

                Power.Reset(true);

            }
            else
            {
                Debug.WriteLine("Could get Time from Internet or RealTime Clock");
                timeIsSet = true;
            }
        }
        #endregion

        #region private method SetupEnc28_SC20260D_MicroBus1
        private static void SetupEnc28_SC20260D_MicroBus1()
        {
            networkController = NetworkController.FromName
                ("GHIElectronics.TinyCLR.NativeApis.ENC28J60.NetworkController");

            var networkInterfaceSetting = new EthernetNetworkInterfaceSettings();

            var networkCommunicationInterfaceSettings = new
                SpiNetworkCommunicationInterfaceSettings();

            var cs = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().
            OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PG12);

            var settings = new GHIElectronics.TinyCLR.Devices.Spi.SpiConnectionSettings()
            {
                ChipSelectLine = cs,
                ClockFrequency = 4000000,
                Mode = GHIElectronics.TinyCLR.Devices.Spi.SpiMode.Mode0,
                ChipSelectType = GHIElectronics.TinyCLR.Devices.Spi.SpiChipSelectType.Gpio,
                ChipSelectHoldTime = TimeSpan.FromTicks(10),
                ChipSelectSetupTime = TimeSpan.FromTicks(10)
            };

            networkCommunicationInterfaceSettings.SpiApiName =
                GHIElectronics.TinyCLR.Pins.SC20260.SpiBus.Spi3;

            networkCommunicationInterfaceSettings.GpioApiName =
                GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.Id;

            networkCommunicationInterfaceSettings.SpiSettings = settings;
            networkCommunicationInterfaceSettings.InterruptPin = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().
                OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PG6);
            networkCommunicationInterfaceSettings.InterruptEdge = GpioPinEdge.FallingEdge;
            networkCommunicationInterfaceSettings.InterruptDriveMode = GpioPinDriveMode.InputPullUp;
            networkCommunicationInterfaceSettings.ResetPin = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().
                OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PI8);
            networkCommunicationInterfaceSettings.ResetActiveState = GpioPinValue.Low;

            networkInterfaceSetting.Address = new IPAddress(new byte[] { 192, 168, 1, 122 });
            networkInterfaceSetting.SubnetMask = new IPAddress(new byte[] { 255, 255, 255, 0 });
            networkInterfaceSetting.GatewayAddress = new IPAddress(new byte[] { 192, 168, 1, 1 });
            networkInterfaceSetting.DnsAddresses = new IPAddress[] { new IPAddress(new byte[]
        { 75, 75, 75, 75 }), new IPAddress(new byte[] { 75, 75, 75, 76 }) };

            networkInterfaceSetting.MacAddress = new byte[] { 0x00, 0x4, 0x00, 0x00, 0x00, 0x00 };
            networkInterfaceSetting.DhcpEnable = true;
            networkInterfaceSetting.DhcpEnable = true;

            networkInterfaceSetting.TlsEntropy = new byte[] { 0, 1, 2, 3 };

            networkController.SetInterfaceSettings(networkInterfaceSetting);
            networkController.SetCommunicationInterfaceSettings
                (networkCommunicationInterfaceSettings);

            networkController.SetAsDefaultController();

            networkController.NetworkAddressChanged += NetworkController_NetworkAddressChanged;
            networkController.NetworkLinkConnectedChanged +=
                NetworkController_NetworkLinkConnectedChanged;

            networkController.Enable();

            while (linkReady == false) ;

            System.Diagnostics.Debug.WriteLine("Network is ready to use");


        }
        #endregion

        #region private method SetupWiFi7Click_SC20100_MicroBus1
        static void SetupWiFi7Click_SC20100_MicroBus1()
        {        
            var enablePin = GpioController.GetDefault().OpenPin(SC20100.GpioPin.PA8);

            enablePin.SetDriveMode(GpioPinDriveMode.Output);
            enablePin.Write(GpioPinValue.High);

            SpiNetworkCommunicationInterfaceSettings networkCommunicationInterfaceSettings =
                new SpiNetworkCommunicationInterfaceSettings();

            
            var cs = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().
                OpenPin(GHIElectronics.TinyCLR.Pins.SC20260.GpioPin.PD15);


            var settings = new GHIElectronics.TinyCLR.Devices.Spi.SpiConnectionSettings()
            {

                ChipSelectLine = cs,
                ClockFrequency = 4000000,
                Mode = GHIElectronics.TinyCLR.Devices.Spi.SpiMode.Mode0,
                ChipSelectType = GHIElectronics.TinyCLR.Devices.Spi.SpiChipSelectType.Gpio,
                ChipSelectHoldTime = TimeSpan.FromTicks(10),
                ChipSelectSetupTime = TimeSpan.FromTicks(10)
            };

            networkCommunicationInterfaceSettings.SpiApiName =
                GHIElectronics.TinyCLR.Pins.SC20100.SpiBus.Spi3;

            networkCommunicationInterfaceSettings.GpioApiName =
                GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.Id;

            networkCommunicationInterfaceSettings.SpiSettings = settings;
            networkCommunicationInterfaceSettings.InterruptPin = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().          
                OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PB12);
            networkCommunicationInterfaceSettings.InterruptEdge = GpioPinEdge.FallingEdge;
            networkCommunicationInterfaceSettings.InterruptDriveMode = GpioPinDriveMode.InputPullUp;
            networkCommunicationInterfaceSettings.ResetPin = GHIElectronics.TinyCLR.Devices.Gpio.GpioController.GetDefault().             
                OpenPin(GHIElectronics.TinyCLR.Pins.SC20100.GpioPin.PB13);
            networkCommunicationInterfaceSettings.ResetActiveState = GpioPinValue.Low;

            var networkController = NetworkController.FromName
                ("GHIElectronics.TinyCLR.NativeApis.ATWINC15xx.NetworkController");

            WiFiNetworkInterfaceSettings networkInterfaceSetting = new WiFiNetworkInterfaceSettings()
            {
                Ssid = wiFiSSID_1,
                Password = wiFiKey_1,
            };

            networkInterfaceSetting.Address = new IPAddress(new byte[] { 192, 168, 1, 122 });
            networkInterfaceSetting.SubnetMask = new IPAddress(new byte[] { 255, 255, 255, 0 });
            networkInterfaceSetting.GatewayAddress = new IPAddress(new byte[] { 192, 168, 1, 1 });
            networkInterfaceSetting.DnsAddresses = new IPAddress[] { new IPAddress(new byte[]
        { 75, 75, 75, 75 }), new IPAddress(new byte[] { 75, 75, 75, 76 }) };

            //networkInterfaceSetting.MacAddress = new byte[] { 0x00, 0x4, 0x00, 0x00, 0x00, 0x00 };
            networkInterfaceSetting.MacAddress = new byte[] { 0x4A, 0x28, 0x05, 0x2A, 0xA4, 0x0F };

            networkInterfaceSetting.DhcpEnable = true;
            networkInterfaceSetting.DhcpEnable = true;

            networkInterfaceSetting.TlsEntropy = new byte[] { 1, 2, 3, 4 };

            networkController.SetInterfaceSettings(networkInterfaceSetting);
            networkController.SetCommunicationInterfaceSettings
                (networkCommunicationInterfaceSettings);

            networkController.SetAsDefaultController();

            networkController.NetworkAddressChanged += NetworkController_NetworkAddressChanged;
            networkController.NetworkLinkConnectedChanged +=
                NetworkController_NetworkLinkConnectedChanged;


            networkController.Enable();


            while (linkReady == false) ;

            // Network is ready to used
        }
        #endregion

        #region private method makePartitionKey

        private static string makePartitionKey(string partitionKeyprefix, bool augmentWithYear)
        {
            // if wanted, augment with year and month (12 - month for right order)     
            return augmentWithYear == true ? partitionKeyprefix + DateTime.Today.Year + "-" + (12 - DateTime.Now.Month).ToString("D2") : partitionKeyprefix;
        }
        #endregion

        #region private method makeRowKey
        private static string makeRowKey(DateTime actDate)
        {
            // formatting the RowKey (= reverseDate) this way to have the tables sorted with last added row upmost
            return (10000 - actDate.Year).ToString("D4") + (12 - actDate.Month).ToString("D2") + (31 - actDate.Day).ToString("D2")
                       + (23 - actDate.Hour).ToString("D2") + (59 - actDate.Minute).ToString("D2") + (59 - actDate.Second).ToString("D2");
        }
        #endregion

    }
}



