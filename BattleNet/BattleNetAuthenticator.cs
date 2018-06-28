using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace CalendarScraper.BattleNet
{
    /// <summary>
    /// Class that implements Battle.net Mobile Authenticator v1.1.0.
    /// </summary>
    public class BattleNetAuthenticator : Authenticator
    {
        /// <summary>
        /// Buffer size used on Http responses
        /// </summary>
        private const int RESPONSE_BUFFER_SIZE = 64;

        /// <summary>
        /// Expected size of return data from time sync
        /// </summary>
        private const int SYNC_RESPONSE_SIZE = 8;

        /// <summary>
        /// Number of digits in code
        /// </summary>
        private const int CODE_DIGITS = 8;

        /// <summary>
        /// Number of minutes to ignore syncing if network error
        /// </summary>
        private const int SYNC_ERROR_MINUTES = 5;

        /// <summary>
        /// URLs for all mobile services
        /// </summary>
        private static string REGION_US = "US";
        private static string REGION_EU = "EU";
        private static string REGION_KR = "KR";
        private static string REGION_CN = "CN";
        public static Dictionary<string, string> MOBILE_URLS = new Dictionary<string, string>
        {
            {REGION_US, "http://mobile-service.blizzard.com"},
            {REGION_EU, "http://mobile-service.blizzard.com"},
            {REGION_KR, "http://mobile-service.blizzard.com"},
            {REGION_CN, "http://mobile-service.battlenet.com.cn"}
        };

        private static string SYNC_PATH = "/enrollment/time.htm";  

        /// <summary>
        /// Time of last Sync error
        /// </summary>
        private static DateTime _lastSyncError = DateTime.MinValue;

        #region Authenticator data

        /// <summary>
        /// Region for authenticator taken from first 2 chars of serial
        /// </summary>
        public string Region
        {
            get
            {
                return (string.IsNullOrEmpty(Serial) == false ? Serial.Substring(0, 2) : string.Empty);
            }
        }

        public string Serial { get; set; }

        /// <summary>
        /// Get/set the combined secret data value
        /// </summary>
        public override string SecretData
        {
            get
            {
                // for Battle.net, this is the key + serial
                return base.SecretData + "|" + Authenticator.ByteArrayToString(Encoding.UTF8.GetBytes(Serial));
            }
            set
            {
                // for Battle.net, extract key + serial
                if (string.IsNullOrEmpty(value) == false)
                {
                    string[] parts = value.Split('|');
                    if (parts.Length <= 1)
                    {
                        // old WinAuth2 version with secretdata + serial
                        SecretKey = Authenticator.StringToByteArray(value.Substring(0, 40));
                        Serial = Encoding.UTF8.GetString(Authenticator.StringToByteArray(value.Substring(40)));
                    }
                    else if (parts.Length == 3) // alpha 3.0.6
                    {
                        // secret|script|serial
                        base.SecretData = value;
                        Serial = (parts.Length > 2 ? Encoding.UTF8.GetString(Authenticator.StringToByteArray(parts[2])) : null);
                    }
                    else
                    {
                        // secret|serial
                        base.SecretData = value;
                        Serial = (parts.Length > 1 ? Encoding.UTF8.GetString(Authenticator.StringToByteArray(parts[1])) : null);
                    }
                }
                else
                {
                    SecretKey = null;
                    Serial = null;
                }
            }
        }

        #endregion

        /// <summary>
        /// Create a new Authenticator object
        /// </summary>
        public BattleNetAuthenticator(string serial, string secret)
            : base(CODE_DIGITS)
        {
            ServerTimeDiff = 0;
            SecretKey = Convert.FromBase64String(secret);
            Serial = serial;
        }

        /// <summary>
        /// Synchronise this authenticator's time with server time. We update our data record with the difference from our UTC time.
        /// </summary>
        public override void Sync()
        {
            // check if data is protected
            if (this.SecretKey == null && this.EncryptedData != null)
            {
                throw new Exception();
            }

            // don't retry for 5 minutes
            if (_lastSyncError >= DateTime.Now.AddMinutes(0 - SYNC_ERROR_MINUTES))
            {
                return;
            }

            try
            {
                // create a connection to time sync server
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(GetMobileUrl(this.Region) + SYNC_PATH);
                request.Method = "GET";
                request.Timeout = 5000;

                // get response
                byte[] responseData = null;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    // OK?
                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new ApplicationException(string.Format("{0}: {1}", (int)response.StatusCode, response.StatusDescription));
                    }

                    // load back the buffer - should only be a byte[8]
                    using (MemoryStream ms = new MemoryStream())
                    {
                        // using (BufferedStream bs = new BufferedStream(response.GetResponseStream()))
                        using (Stream bs = response.GetResponseStream())
                        {
                            byte[] temp = new byte[RESPONSE_BUFFER_SIZE];
                            int read;
                            while ((read = bs.Read(temp, 0, RESPONSE_BUFFER_SIZE)) != 0)
                            {
                                ms.Write(temp, 0, read);
                            }
                            responseData = ms.ToArray();

                            // check it is correct size
                            if (responseData.Length != SYNC_RESPONSE_SIZE)
                            {
                                throw new Exception(string.Format("Invalid response data size (expected " + SYNC_RESPONSE_SIZE + " got {0}", responseData.Length));
                            }
                        }
                    }
                }

                // return data:
                // 00-07 server time (Big Endian)

                // extract the server time
                if (BitConverter.IsLittleEndian == true)
                {
                    Array.Reverse(responseData);
                }
                // get the difference between the server time and our current time
                long serverTimeDiff = BitConverter.ToInt64(responseData, 0) - CurrentTime;

                // update the Data object
                ServerTimeDiff = serverTimeDiff;
                LastServerTime = DateTime.Now.Ticks;

                // clear any sync error
                _lastSyncError = DateTime.MinValue;
            }
            catch (WebException)
            {
                // don't retry for a while after error
                _lastSyncError = DateTime.Now;

                // set to zero to force reset
                ServerTimeDiff = 0;
            }
        }



        /// <summary>
        /// Get the base mobil url based on the region
        /// </summary>
        /// <param name="region">two letter region code, i.e US or CN</param>
        /// <returns>string of Url for region</returns>
        private static string GetMobileUrl(string region)
        {
            string upperregion = region.ToUpper();
            if (upperregion.Length > 2)
            {
                upperregion = upperregion.Substring(0, 2);
            }
            if (MOBILE_URLS.ContainsKey(upperregion) == true)
            {
                return MOBILE_URLS[upperregion];
            }
            else
            {
                return MOBILE_URLS[REGION_US];
            }
        }
    }
}