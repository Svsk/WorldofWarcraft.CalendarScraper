using System;
using System.Linq;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Digests;

namespace CalendarScraper.BattleNet
{
    /// <summary>
    /// Class that implements base RFC 4226 an RFC 6238 authenticator
    /// </summary>
    public abstract class Authenticator
    {
        /// <summary>
        /// Default number of digits in code
        /// </summary>
        public const int DEFAULT_CODE_DIGITS = 6;

        /// <summary>
        /// Default period of 30s
        /// </summary>
        public const int DEFAULT_PERIOD = 30;

        /// <summary>
        /// HMAC hashing algorithm types
        /// </summary>
        public enum HMACTypes
        {
            SHA1 = 0,
            SHA256 = 1,
            SHA512 = 2
        }

        #region Authenticator data

        /// <summary>
        /// Secret key used for Authenticator
        /// </summary>
        public byte[] SecretKey { get; set; }

        /// <summary>
        /// Time difference in milliseconds of our machine and server
        /// </summary>
        public long ServerTimeDiff { get; set; }

        /// <summary>
        /// Time of last synced
        /// </summary>
        public long LastServerTime { get; set; }

        /// <summary>
        /// The data current saved with the current encryption and/or password (might be none)
        /// </summary>
        protected string EncryptedData { get; private set; }

        /// <summary>
        /// Number of digits returned in code (default is 6)
        /// </summary>
        public int CodeDigits { get; set; }

        /// <summary>
        /// Hashing algorithm used for OTP generation (default is SHA1)
        /// </summary>
        public HMACTypes HMACType { get; set; }

        /// <summary>
        /// Period in seconds for next code
        /// </summary>
        public int Period { get; set; }

        /// <summary>
        /// Get/set the combined secret data value
        /// </summary>
        public virtual string SecretData
        {
            get
            {
                // this is the secretkey
                return Authenticator.ByteArrayToString(SecretKey) + "\t" + this.CodeDigits.ToString() + "\t" + this.HMACType.ToString() + "\t" + this.Period.ToString();
            }
            set
            {
                if (string.IsNullOrEmpty(value) == false)
                {
                    string[] parts = value.Split('|')[0].Split('\t');
                    SecretKey = Authenticator.StringToByteArray(parts[0]);
                    if (parts.Length > 1)
                    {
                        int digits;
                        if (int.TryParse(parts[1], out digits) == true)
                        {
                            CodeDigits = digits;
                        }
                    }
                    if (parts.Length > 2)
                    {
                        HMACType = (HMACTypes)Enum.Parse(typeof(HMACTypes), parts[2]);
                    }
                    if (parts.Length > 3)
                    {
                        int period;
                        if (int.TryParse(parts[3], out period) == true)
                        {
                            Period = period;
                        }
                    }
                }
                else
                {
                    SecretKey = null;
                }
            }
        }

        /// <summary>
        /// Calculate the code interval based on the calculated server time
        /// </summary>
        public long CodeInterval
        {
            get
            {
                // calculate the code interval; the server's time div 30,000
                return (CurrentTime + ServerTimeDiff) / ((long)this.Period * 1000L);
            }
        }

        /// <summary>
        /// Get the current code for the authenticator.
        /// </summary>
        /// <returns>authenticator code</returns>
        public string CurrentCode
        {
            get
            {
                if (this.SecretKey == null && this.EncryptedData != null)
                {
                    throw new Exception();
                }

                return CalculateCode(false);
            }
        }

        #endregion

        /// <summary>
        /// Create a new Authenticator object
        /// </summary>
        public Authenticator(int codeDigits = DEFAULT_CODE_DIGITS, HMACTypes hmacType = HMACTypes.SHA1, int period = DEFAULT_PERIOD)
        {
            CodeDigits = codeDigits;
            HMACType = hmacType;
            Period = period;
        }

        /// <summary>
        /// Calculate the current code for the authenticator.
        /// </summary>
        /// <param name="resyncTime">flag to resync time</param>
        /// <returns>authenticator code</returns>
        protected virtual string CalculateCode(bool resync = false, long interval = -1)
        {
            // sync time if required
            if (resync == true || ServerTimeDiff == 0)
            {
                if (interval > 0)
                {
                    ServerTimeDiff = (interval * ((long)this.Period * 1000L)) - CurrentTime;
                }
                else
                {
                    Sync();
                }
            }

            HMac hmac;
            switch (HMACType)
            {
                case HMACTypes.SHA1:
                    hmac = new HMac(new Sha1Digest());
                    break;
                case HMACTypes.SHA256:
                    hmac = new HMac(new Sha256Digest());
                    break;
                case HMACTypes.SHA512:
                    hmac = new HMac(new Sha512Digest());
                    break;
                default:
                    throw new Exception();
            }
            hmac.Init(new KeyParameter(SecretKey));

            byte[] codeIntervalArray = BitConverter.GetBytes(CodeInterval);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(codeIntervalArray);
            }
            hmac.BlockUpdate(codeIntervalArray, 0, codeIntervalArray.Length);

            byte[] mac = new byte[hmac.GetMacSize()];
            hmac.DoFinal(mac, 0);

            // the last 4 bits of the mac say where the code starts (e.g. if last 4 bit are 1100, we start at byte 12)
            int start = mac.Last() & 0x0f;

            // extract those 4 bytes
            byte[] bytes = new byte[4];
            Array.Copy(mac, start, bytes, 0, 4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            uint fullcode = BitConverter.ToUInt32(bytes, 0) & 0x7fffffff;

            // we use the last 8 digits of this code in radix 10
            uint codemask = (uint)Math.Pow(10, CodeDigits);
            string format = new string('0', CodeDigits);
            string code = (fullcode % codemask).ToString(format);

            return code;
        }

        /// <summary>
        /// Synchorise this authenticator's time with server time. We update our data record with the difference from our UTC time.
        /// </summary>
        public abstract void Sync();

   

        #region Utility functions

       
        /// <summary>
        /// Get the milliseconds since 1/1/70 (same as Java currentTimeMillis)
        /// </summary>
        public static long CurrentTime
        {
            get
            {
                return Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds);
            }
        }

        /// <summary>
        /// Convert a hex string into a byte array. E.g. "001f406a" -> byte[] {0x00, 0x1f, 0x40, 0x6a}
        /// </summary>
        /// <param name="hex">hex string to convert</param>
        /// <returns>byte[] of hex string</returns>
        public static byte[] StringToByteArray(string hex)
        {
            int len = hex.Length;
            byte[] bytes = new byte[len / 2];
            for (int i = 0; i < len; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
        }

        /// <summary>
        /// Convert a byte array into a ascii hex string, e.g. byte[]{0x00,0x1f,0x40,ox6a} -> "001f406a"
        /// </summary>
        /// <param name="bytes">byte array to convert</param>
        /// <returns>string version of byte array</returns>
        public static string ByteArrayToString(byte[] bytes)
        {
            // Use BitConverter, but it sticks dashes in the string
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

       

        #endregion
    }
}