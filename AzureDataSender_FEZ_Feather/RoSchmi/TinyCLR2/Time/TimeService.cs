using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using GHIElectronics.TinyCLR.Devices.Network;
using GHIElectronics.TinyCLR.Native;

namespace RoSchmi.TinyCLR2.Time
{
    /// <summary>
    /// This is an adaption of the "FixedTimeService" for NETMF posted by @eolson in GHI Codeshare
    /// 
    /// Comments of @eolson in his original post:
    /// Fixed TimeService
    ///    TimeService class replacement that adds DST support and that fixes several issues.
    /// While this is a direct replacement for TimeService, it could easily be rewritten for
    /// non-premium gadgeteer libraries (it was originally Fez Panda code that was migrated)
    /// 
    /// The DST settings are done by defining the staring & ending dates, as well as an offset. 
    /// The format is the same as the tz_database:  
    ///     US: "Mar Sun>=8 @2", "Nov Sun>=1 @2", 60 
    ///     EU: "Mar lastSun @1", "Oct lastSun @1", 60
    ///     Arizona: "Mar Sun>=8 @2", "Nov Sun>=1 @2", 0 (no offset) .. or don't have AutoDaylightSavings
    ///     
    /// For me, the standard TimeService class would freeze (block all threads) for about 40 
    /// seconds when it did a ntp request. My GetTimeFromNTP method always gets a response right away
    /// using the same server (and does not block other threads). Note: I do send the udp data
    /// packet TWICE! If I only send it once, it only works about 5% of the time. I don't know 
    /// if it my ISP (comcast) or my Cisco router.. but sending the data twice works great!
    /// (I had exactly the same issue with both a Fez Panda II and Fez Cobra II) 
    /// </summary>
    public static class TimeService
    {
        public delegate void SystemTimeChangedEventHandler(object sender, SystemTimeChangedEventArgs e);
        public delegate void TimeSyncFailedEventHandler(object sender, TimeSyncFailedEventArgs e);


        private static TimeServiceStatus status = new TimeServiceStatus();
        public static TimeServiceStatus LastSyncStatus
        {
            get { return status; }
        }
        public static bool GoodSyncStatus
        {
            get
            {
                return ((status.SyncSourceServer != 0) &&
              (status.Flags == TimeServiceStatus.TimeServiceStatusFlags.SyncSucceeded));
            }
        }

        public static TimeServiceSettings Settings { get; set; }

        public static int timeout = 5; // 5 second timeout

        public static event SystemTimeChangedEventHandler SystemTimeChanged = delegate { };  // time changed because of ntp sync
        public static event SystemTimeChangedEventHandler SystemTimeChecked = delegate { };  // time verified with ntp sync
        public static event TimeSyncFailedEventHandler TimeSyncFailed = delegate { };

        private static UInt32 errorCode = 0;
        public const uint CLR_E_TIMEOUT = 0x80000000 | 0x7E000000 | 0x0000;

        private static int timeZoneOffset = 0;
        public static void SetTimeZoneOffset(int offsetInMinutes)
        {
            timeZoneOffset = offsetInMinutes;
        }
        /*  USA
        private static int dstOffset = 60; // 1 hour (US 2013)
        private static string dstStart = "Mar Sun>=8"; // 2nd Sunday March (US 2013)
        private static string dstEnd = "Nov Sun>=1"; // 1st Sunday Nov (US 2013)
        */

        // Europe
        private static int dstOffset = 60; // 1 hour (Europe 2016)
        private static string dstStart = "Mar lastSun @1";
        private static string dstEnd = "Oct lastSun @1";


        /// <summary>
        /// Set dst information
        /// </summary>
        /// <remarks>
        ///  start/end strings are 'tz' format: "Mth Day>=n" or "Mth lastDay"
        ///   e.g. "Mar Sun>=8", "Nov Sun>=1", "Oct lastSun", "Feb 23"
        ///   (see http://en.wikipedia.org/wiki/Zoneinfo for tz_database info)
        /// </remarks>
        /// <param name="start">dst starting day</param>
        /// <param name="end">dst ending day</param>
        /// <param name="offsetInMinutes">dst offset in minutes</param>
        public static void SetDst(string start, string end, int offsetInMinutes)
        {
            dstStart = start;
            dstEnd = end;
            dstOffset = offsetInMinutes;
        }

        /// <summary>
        /// Sets the UTC time of the system.
        /// </summary>
        /// <param name="utcTime"></param>
        
        public static void SetUtcTime(DateTime utcTime)
        {
            SystemTime.SetTime(utcTime, timeZoneOffset);          
        }

        private static System.Threading.Timer timeSyncTimer = null;
        public static void Start()
        {
            DateTime past = new DateTime(2019, 1, 1);
            if ((DateTime.Now < past) && (Settings.ForceSyncAtWakeUp))
            {
                AutoUpdate(null);
            }
            timeSyncTimer = new System.Threading.Timer(new System.Threading.TimerCallback(AutoUpdate), null,
                    TimeSpan.FromTicks(TimeSpan.TicksPerSecond * Settings.RefreshTime),
                    TimeSpan.FromTicks(TimeSpan.TicksPerSecond * Settings.RefreshTime));
        }

        public static void Stop()
        {
            timeSyncTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        }

        private static void AutoUpdate(object state)
        {
            UpdateNow(Settings.Tolerance);
        }

        public static TimeServiceStatus UpdateNow(uint tolerance)
        {
            DateTime ntpTime = GetTimeFromNTP(Settings.PrimaryServer);

            if (ntpTime == DateTime.MinValue)
                ntpTime = GetTimeFromNTP(Settings.AlternateServer);

            // Changed by RoSchmi
            Thread.Sleep(20);

            // retry - not sure why, but this works the second time (2 x timeout from first query = 10 seconds) 
            if (ntpTime == DateTime.MinValue)
                ntpTime = GetTimeFromNTP(Settings.PrimaryServer);

            if (ntpTime == DateTime.MinValue)
                ntpTime = GetTimeFromNTP(Settings.AlternateServer);

            return CheckResults(tolerance, ntpTime);
        }

        public static TimeServiceStatus UpdateNow(byte[] serverAddress, uint tolerance)
        {

            DateTime ntpTime = GetTimeFromNTP(serverAddress);
            if (ntpTime == DateTime.MinValue)
                ntpTime = GetTimeFromNTP(serverAddress);  // try again (1 timeout = 5 seconds later)

            return CheckResults(tolerance, ntpTime);
        }

        // network changed callback
        public static void ForcedSync(object sender, EventArgs e)
        {
            if (Settings.ForceSyncAtWakeUp)
            {
                UpdateNow(Settings.Tolerance);
            }
        }

        // check and return status
        private static TimeServiceStatus CheckResults(uint tolerance, DateTime ntpTime)
        {
            if (ntpTime == DateTime.MinValue)
            {
                // fail: call failEvent 
                status.Flags = TimeServiceStatus.TimeServiceStatusFlags.SyncFailed;
                TimeSyncFailedEventArgs failEvent = new TimeSyncFailedEventArgs(DateTime.Now, errorCode);
                TimeSyncFailed(null, failEvent);
            }
            else
            {
                // succeed: if (change > tolerance) then set time & call changed Event
                status.Flags = TimeServiceStatus.TimeServiceStatusFlags.SyncSucceeded;
                TimeSpan syncTimeOffset = DateTime.Now - ntpTime;
                status.SyncTimeOffset = (syncTimeOffset.Ticks / TimeSpan.TicksPerMillisecond);
                if (System.Math.Abs(status.SyncTimeOffset) > tolerance)
                {
                    
                    SetUtcTime(ntpTime.AddMinutes( - timeZoneOffset));
                    SystemTimeChangedEventArgs changedEvent = new SystemTimeChangedEventArgs(DateTime.Now);
                    SystemTimeChanged(null, changedEvent);
                }
                SystemTimeChangedEventArgs checkedEvent = new SystemTimeChangedEventArgs(DateTime.Now);
                SystemTimeChecked(null, checkedEvent);
            }
            return status;
        }

        public static DateTime GetTimeFromNTP(byte[] serverAddress)
        {
            errorCode = 0;
            long ip;

           

            try
            {
                if (serverAddress != null)
                {
                    ip = 16777216 * (long)serverAddress[0] + 65536 * (long)serverAddress[1]
                              + 256 * (long)serverAddress[2] + (long)serverAddress[3];
                }
                else
                {
                    return DateTime.MinValue;
                }
            }
            catch
            {
                return DateTime.MinValue;
            }
            status.SyncSourceServer = (uint)ip;

            using (Socket ntpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                //Socket ntpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                try
                {
                    EndPoint remoteEP = new IPEndPoint(new IPAddress(serverAddress), 123);

                    // init request
                    byte[] ntpData = new byte[48];
                    Array.Clear(ntpData, 0, 48);
                    ntpData[0] = 0x1B; // set protocol version

                    // send request              
                    ntpSocket.SendTo(ntpData, remoteEP);   // do this twice to make reliable -- not sure why, but it makes HUGE difference
                    ntpSocket.SendTo(ntpData, remoteEP);

                    // wait, if no response, timeout
                    if (ntpSocket.Poll(timeout * 1000 * 1000, SelectMode.SelectRead))
                    {
                        // get response
                        ntpSocket.ReceiveFrom(ntpData, ref remoteEP);
                        ntpSocket.Close();

                        // parse time value
                        byte offsetTransmitTime = 40;
                        ulong intpart = 0;
                        ulong fractpart = 0;
                        for (int i = 0; i <= 3; i++) intpart = (intpart << 8) | ntpData[offsetTransmitTime + i];
                        for (int i = 4; i <= 7; i++) fractpart = (fractpart << 8) | ntpData[offsetTransmitTime + i];
                        ulong milliseconds = (intpart * 1000 + (fractpart * 1000) / 0x100000000L);

                        DateTime ntpTime = new DateTime(1900, 1, 1) + TimeSpan.FromTicks((long)milliseconds * TimeSpan.TicksPerMillisecond);

                        ntpTime = ntpTime.AddMinutes(timeZoneOffset);  // universal => local

                        if (Settings.AutoDayLightSavings && IsDST(ntpTime))
                            ntpTime = ntpTime.AddMinutes(dstOffset);

                        return ntpTime;
                    }
                    else
                    {
                        // timeout
                        errorCode = CLR_E_TIMEOUT;
                        ntpSocket.Close();
                    }
                }
                catch (Exception ex)
                {
                    // get errorCode from exception type
                    Type exceptionType = typeof(Exception);
                    System.Reflection.FieldInfo hresultFieldInfo =
                        exceptionType.GetField("m_HResult", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    errorCode = hresultFieldInfo != null ? (uint)hresultFieldInfo.GetValue(ex) : 0;

                    try
                    {
                        if (ntpSocket != null)
                            ntpSocket.Close();
                    }
                    catch { }
                }
            }

            return DateTime.MinValue;
        }

        private static bool IsDST(DateTime today)
        {
            DateTime dstStartDay = GetDstDate(dstStart, today);
            DateTime dstEndDay = GetDstDate(dstEnd, today);

            if (dstStartDay <= dstEndDay)
            {   // northern hem
                if (today < dstStartDay)
                    return false; // before dsl
                else if (today >= dstEndDay)
                    return false; // after dsl
                else
                    return true; // during dsl 
            }
            else
            {   // southern hem
                if (today < dstEndDay)
                    return true; // still dsl
                else if (today >= dstStartDay)
                    return true; // started dsl
                else
                    return false; // not in dsl
            }
        }

        private static string[] Months = new string[] { "???", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private static string[] Days = new string[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

        //  decode dstDateStrings into DateTimes:    "Mth Day>=n" or "Mth lastDay"
        //   e.g. "Mar Sun>=8", "Nov Sun>=1", "Oct Sun>=1", "Apr Sun>=1", "Oct lastSun", "Feb 26"
        //        to change the time, append " @dd":  "Mar Sun>=8 @2" (US start)  "Mar lastSun @1" (EU start)
        private static DateTime GetDstDate(string dstDateString, DateTime today)
        {
            // adjust for exact time of change if not 2
            int timeOfChange = 2; // hours
            int spaceAt = dstDateString.IndexOf(" @");
            if (spaceAt > 0)
            {
                timeOfChange = Convert.ToInt32(dstDateString.Substring(spaceAt + 2));
                dstDateString = dstDateString.Substring(0, spaceAt);
            }

            DateTime dstDate = DateTime.MinValue;
            int year = today.Year;
            int month = 0;
            double doubleDate = 0;
            for (int i = 0; i < Months.Length; i++)
                if (Months[i] == dstDateString.Substring(0, 3))
                    month = i;

            if (dstDateString.Substring(4, 4) == "last")
            {
                int day = 0;
                for (int i = 0; i < Days.Length; i++)
                    if (Days[i] == dstDateString.Substring(8, 3))
                        day = i;
                if (month == 12)   // improbable case
                {
                    month = 0;
                    year++;
                }
                dstDate = new DateTime(year, month + 1, 1).AddDays(-1); // last day of month
                int lastDay = (int)dstDate.DayOfWeek;
                dstDate = dstDate.AddDays(lastDay >= day ? day - lastDay : day - lastDay - 7);
            }
            else if (dstDateString.Substring(7, 2) == ">=")
            {
                int day = 0;
                for (int i = 0; i < Days.Length; i++)
                    if (Days[i] == dstDateString.Substring(4, 3))
                        day = i;
                int date = Convert.ToInt32(dstDateString.Substring(9));
                dstDate = new DateTime(year, month, date);   // minimum day
                int minDay = (int)dstDate.DayOfWeek;
                dstDate = dstDate.AddDays((7 + day - minDay) % 7);
            }
            else if (double.TryParse(dstDateString.Substring(4), out doubleDate))
            {
                dstDate = new DateTime(year, month, (int)doubleDate);
            }
            dstDate = dstDate.AddHours(timeOfChange);
            return dstDate;
        }
    }
    public class SystemTimeChangedEventArgs : EventArgs
    {
        public readonly DateTime EventTime;

        public SystemTimeChangedEventArgs(DateTime eventTime) { }
    }

    public class TimeSyncFailedEventArgs : EventArgs
    {
        public readonly uint ErrorCode;
        public readonly DateTime EventTime;

        public TimeSyncFailedEventArgs(DateTime eventTime, uint errorCode) { }
    }

    public class TimeServiceSettings
    {
        protected uint AlternateServerIP;
        public bool AutoDayLightSavings;
        public bool ForceSyncAtWakeUp;
        protected uint PrimaryServerIP;
        public uint RefreshTime;
        public uint Tolerance;

        public TimeServiceSettings() { }

        public byte[] AlternateServer { get; set; }
        public byte[] PrimaryServer { get; set; }
    }

    public class TimeServiceStatus
    {
        public TimeServiceStatus.TimeServiceStatusFlags Flags;
        public uint SyncSourceServer;
        public long SyncTimeOffset;
        protected long TimeUTC;

        public TimeServiceStatus() { }

        public DateTime CurrentTimeUTC { get; }

        [Flags]
        public enum TimeServiceStatusFlags
        {
            SyncSucceeded = 0,
            SyncFailed = 1,
        }
    }
}
