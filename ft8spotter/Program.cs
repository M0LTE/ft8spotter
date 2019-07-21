using Dapper;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ft8spotter
{
    class Program
    {
        private const string configKey = "cloudlog_connection_string";

        static ClublogCtyXml ctyXml;
        static void Main(string[] args)
        {
            if (args.Any(a => a == "--help" || a == "-h" || a == "/?"))
            {
                Console.WriteLine(@"A work in progress, that listens to udp://localhost:2237 for WSJT-X, works out the DXCC entity of every call 
                                    heard using Clublog's cty.xml, then queries a Cloudlog MySQL database directly (because there's no API yet)
                                    to see if it's a needed slot. If it is, it highlights the call in red in the console window.");
                return;
            }

            if (!File.Exists(configFile))
            {
                Console.WriteLine("Cloudlog connection string?");
                string cs = Console.ReadLine();
                File.WriteAllText(configFile, $"{configKey}={cs}");
            }

            ctyXml = ClublogCtyXml.Parse(File.ReadAllText("cty.xml"));

            using (var client = new UdpClient(2237, AddressFamily.InterNetwork))
            {
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    var ipep = new IPEndPoint(IPAddress.Loopback, 0);

                    byte[] msg = client.Receive(ref ipep);
                    if (msg[11] == 0x02)
                    {
                        string heardCall = GetHeardCall(msg);

                        if (heardCall == null)
                            continue;

                        var entity = GetEntity(heardCall);

                        if (sw.Elapsed > TimeSpan.FromSeconds(5))
                        {
                            Console.WriteLine("------------------------------------------------------");
                            sw.Restart();
                        }

                        bool needed = entity == null ? false : GetNeeded(20, entity.Adif);

                        var colBefore = Console.ForegroundColor;
                        if (needed)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                        }
                        Console.WriteLine($"{heardCall} - {entity?.Entity ?? "unknown"}");
                        Console.ForegroundColor = colBefore;
                    }
                }
            }
        }

        static string configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ft8spotter", "config");
        static string cs;
        static IDbConnection GetConnection()
        {
            if (cs == null)
            {
                var configSettings = File.ReadAllLines(configFile)
                    .Where(line => line.Contains("="))
                    .Select(line => new { key = line.Substring(0, line.IndexOf("=")), value = line.Substring(line.IndexOf("=") + 1) })
                    .Where(kvp => !String.IsNullOrWhiteSpace(kvp.key) && !String.IsNullOrWhiteSpace(kvp.value))
                    .ToDictionary(line => line.key, line => line.value);

                cs = configSettings[configKey];
            }

            return new MySqlConnection(cs);
        }

        private static bool GetNeeded(int band, int? adif)
        {
            if (!adif.HasValue)
                return false;

            int dxcc = adif.Value;

            using (var conn = GetConnection())
            {
                conn.Open(); 
                // and col_mode = @mode
                return 0 == conn.ExecuteScalar<int>("select count(1) from TABLE_HRD_CONTACTS_V01 where col_dxcc=@dxcc and COL_BAND = @band", new { mode = "FT8", band = $"{band}M", dxcc });
            }
        }

        private static MatchingEntity GetEntity(string heardCall)
        {
            var possibleExceptions = ctyXml.Exceptions.Where(c => String.Equals(heardCall, c.Call, StringComparison.OrdinalIgnoreCase));

            foreach (var possibleException in possibleExceptions)
            {
                if (possibleException.Start.HasValue)
                {
                    if (possibleException.Start.Value.ToUniversalTime() > DateTime.UtcNow)
                    {
                        continue;
                    }
                    else
                    {
                        // matches
                    }
                }

                if (possibleException.End.HasValue)
                {
                    if (possibleException.End.Value.ToUniversalTime() < DateTime.UtcNow)
                    {
                        continue;
                    }
                    else
                    {
                        // matches
                    }
                }

                return new MatchingEntity
                {
                    Call = possibleException.Call,
                    Adif = possibleException.Adif,
                    Continent = possibleException.Continent,
                    CqZone = possibleException.CqZone,
                    Entity = possibleException.Entity,
                    Latitude = possibleException.Latitude,
                    Longitude = possibleException.Longitude,
                };
            }

            var fragments = GetFragments(heardCall);

            foreach (var callFragement in fragments)
            {
                var possiblePrefixes = ctyXml.Prefixes.Where(p => String.Equals(callFragement, p.Call, StringComparison.OrdinalIgnoreCase));

                foreach (var possiblePrefix in possiblePrefixes)
                {
                    if (possiblePrefix.Start.HasValue)
                    {
                        if (possiblePrefix.Start.Value.ToUniversalTime() > DateTime.UtcNow)
                        {
                            continue;
                        }
                        else
                        {
                            // matches
                        }
                    }

                    if (possiblePrefix.End.HasValue)
                    {
                        if (possiblePrefix.End.Value.ToUniversalTime() < DateTime.UtcNow)
                        {
                            continue;
                        }
                        else
                        {
                            // matches
                        }
                    }

                    return new MatchingEntity
                    {
                        Call = possiblePrefix.Call,
                        Adif = possiblePrefix.Adif,
                        Continent = possiblePrefix.Continent,
                        CqZone = possiblePrefix.CqZone,
                        Entity = possiblePrefix.Entity,
                        Latitude = possiblePrefix.Latitude,
                        Longitude = possiblePrefix.Longitude,
                    };
                }
            }

            return null;
        }

        private static IEnumerable<string> GetFragments(string call)
        {
            for (int i = call.Length; i > 0; i--)
            {
                yield return call.Substring(0, i);
            }
        }

        static string GetHeardCall(byte[] msg)
        {
            /*int cur = 0;
            foreach (var batch in msg.Batch(8))
            {
                Console.Write(cur.ToString("00") + " ");
                foreach (var b in batch)
                {
                    string bytestr = b.ToString("X").ToLower();

                    if (bytestr.Length == 1)
                    {
                        bytestr = "0" + bytestr;
                    }

                    Console.Write(bytestr + " ");
                }
                Console.WriteLine();

                Console.Write("   ");
                foreach (var b in batch)
                {
                    char ch = (char)b;
                    if (Char.IsLetterOrDigit(ch) || Char.IsPunctuation(ch) || Char.IsSymbol(ch) || (ch == ' '))
                    {
                        Console.Write(ch);
                        Console.Write("  ");
                    }
                }
                Console.WriteLine();
                Console.WriteLine();
                cur += 8;
            }*/

            string text;
            try
            {
                text = Encoding.ASCII.GetString(msg.Skip(52).SkipLast(2).ToArray());
            }
            catch (Exception)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string[] split = text.Split(' ');

            string heard;
            if (split.Length == 0)
            {
                heard = null;
            }
            else if (split.Length == 1)
            {
                heard = split[0];
            }
            else if (split.Length == 2)
            {
                heard = split[split.Length - 1];
            }
            else if (split.Length == 3)
            {
                heard = split[split.Length - 2];
            }
            else if (split.Length == 4)
            {
                heard = split[split.Length - 2];
            }
            else
            {
                heard = null;
            }

            if (heard != null)
            {
                heard = heard.Replace("<", "").Replace(">", "");
            }

            return heard;
        }
    }

    internal class MatchingEntity
    {
        public string Call { get; set; }
        public int? Adif { get; set; }
        public string Continent { get; set; }
        public int? CqZone { get; set; }
        public string Entity { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public static class Extensions
    {
        public static IEnumerable<IEnumerable<TSource>> Batch<TSource>(
                  this IEnumerable<TSource> source, int size)
        {
            TSource[] bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                if (bucket == null)
                    bucket = new TSource[size];

                bucket[count++] = item;
                if (count != size)
                    continue;

                yield return bucket;

                bucket = null;
                count = 0;
            }

            if (bucket != null && count > 0)
                yield return bucket.Take(count);
        }
    }
}
