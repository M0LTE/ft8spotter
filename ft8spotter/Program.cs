﻿using AmateurBandLib;
using M0LTE.WsjtxUdpLib.Client;
using M0LTE.WsjtxUdpLib.Messages.Out;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace ft8spotter
{
    class Program
    {
        private const string connectionStringKey = "cloudlog_connection_string";
        private const string urlKey = "cloudlog_url";

        static ClublogCtyXml ctyXml;
        static int bandMetres;
        static void Main(string[] args)
        {
            httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("ft8spotter");

            bool multicast = GetAndConsumeArg(args, "--multicast");

            bool all = args.Any(a => a == "--all");

            bool grids = args.Any(a => a == "--grids");

            if (!all)
            {
                Console.WriteLine("Only showing needed spots. Pass --all for all spots.");
            }

            if (!grids)
            {
                Console.WriteLine("Not looking for unworked grids. Pass --grids to turn this on.");
            }

            if (args.Any(a => a == "--help" || a == "-h" || a == "/?"))
            {
                Console.WriteLine(@"A work in progress, that listens to udp://localhost:2237 for WSJT-X, works out the DXCC entity of every call 
heard using Clublog's cty.xml, then queries a Cloudlog instance via its API to see if it's a needed slot. If
it is, it highlights the call in red in the console window.");
                return;
            }

            if (!File.Exists(configFile) || File.ReadAllText(configFile).Contains(connectionStringKey))
            {
                Console.WriteLine("You need to provide a Cloudlog URL, e.g. https://mycloudloginstance.net");
                Console.WriteLine("in order for ft8spotter to check spots against Cloudlog. Please provide it now...");
                string url = Console.ReadLine();

                string dir = Path.GetDirectoryName(configFile);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(configFile, $"{urlKey}={url}");
            }

            var config = GetConfig();

            cloudLogUri = new Uri(new Uri(config[urlKey]), "index.php/api/");

            var fi = new FileInfo("cty.xml");

            bool isFresh = false;
            if (DateTime.Now - fi.LastWriteTime > TimeSpan.FromDays(1))
            {
                var wc = new WebClient();
                wc.DownloadFile("https://cdn.clublog.org/cty.php?api=a11c3235cd74b88212ce726857056939d52372bd&zip=1", "cty_new.xml.zip");
                if (File.Exists("cty.xml.bak"))
                {
                    File.Delete("cty.xml.bak");
                }
                File.Move("cty.xml", "cty.xml.bak");
                ZipFile.ExtractToDirectory("cty_new.xml.zip", ".");
                isFresh = true;
                File.Delete("cty_new.xml.zip");
            }

            try
            {
                ctyXml = ClublogCtyXml.Parse(File.ReadAllText("cty.xml"));
            }
            catch (Exception ex)
            {
                if (isFresh)
                {
                    File.Delete("cty.xml");
                    File.Move("cty.xml.bak", "cty.xml");
                    Console.WriteLine($"Failed to update cty.xml: {ex.Message}");
                    ctyXml = ClublogCtyXml.Parse(File.ReadAllText("cty.xml"));
                }
            }

            WsjtxClient wsjsxClient;
            if (multicast)
            {
                //TODO: make hard-coded group an argument
                wsjsxClient = new WsjtxClient(Callback, IPAddress.Parse("239.1.2.3"), multicast: true);
            }
            else
            {
                wsjsxClient = new WsjtxClient(Callback, IPAddress.Loopback);
            }

            Console.WriteLine($"Listening for WSJT-X");

            Thread.CurrentThread.Join();

            void Callback(WsjtxMessage wsjtxMessage)
            {
                if (wsjtxMessage is StatusMessage statusMessage)
                {
                    ProcessStatusMessage(statusMessage);
                    return;
                }

                if (!(wsjtxMessage is DecodeMessage decodeMessage))
                {
                    return;
                }

                if (bandMetres == 0)
                {
                    return;
                }

                var sw = Stopwatch.StartNew();
                string heardCall = GetHeardCall(decodeMessage.Message);

                if (heardCall == null)
                {
                    return;
                }

                var entity = GetEntity(heardCall);

                string grid = GetGrid(decodeMessage.Datagram);

                var needed = entity == null ? Needed.No : GetNeeded(bandMetres, entity.Adif, grids ? grid : null, "ft8");

                if (all || !Needed.No.Equals(needed))
                {
                    if (sw.Elapsed > TimeSpan.FromSeconds(5))
                    {
                        Console.WriteLine($"---  {DateTime.Now:HH:mm:ss}  --------------------------");
                        sw.Restart();
                    }

                    var colBefore = Console.ForegroundColor;
                    if (needed.NewCountryOnAnyBand)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                    }
                    else if (needed.NewCountryOnBand)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }
                    else if (needed.NewCountryOnBandOnMode)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                    }
                    else if (needed.NewGridOnAnyBand)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                    }
                    else if (needed.NewGridOnBand)
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                    }
                    else if (needed.NewGridOnBandOnMode)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                    }
                    WriteAtColumn(0, needed, 19);
                    WriteAtColumn(19, heardCall, 10);
                    WriteAtColumn(30, decodeMessage.Snr, 4);
                    WriteAtColumn(34, IsGrid(grid) ? grid : String.Empty, 4);
                    WriteAtColumn(39, entity?.Adif, 3);
                    WriteAtColumn(43, (entity?.Entity) ?? "Unknown", 50);

                    Console.WriteLine();
                    Console.ForegroundColor = colBefore;
                }
            }
        }

        private static void ProcessStatusMessage(StatusMessage statusMessage)
        {
            var band = AmateurBand.FromHz((long)statusMessage.DialFrequency);

            if (band == null)
            {
                Console.WriteLine($"Not an amateur band: {statusMessage.DialFrequency / 1000000.0}MHz");
                return;
            }

            if (int.TryParse(band.Name.Replace("m", ""), out int bm) && bm != bandMetres)
            {
                bandMetres = bm;
                Console.WriteLine($"Switched to {band.Name}");
            }
        }

        private static bool GetAndConsumeArg(string[] args, string v)
        {
            bool present = args.Contains(v);
            args = args.Where(a => a != v).ToArray();
            return present;
        }

        private static string GetHeardCall(string text)
        {
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

        private static void WriteAtColumn(int col, object heardCall, int max)
        {
            Console.SetCursorPosition(col, Console.CursorTop);
            string toWrite;
            if (heardCall == null)
            {
                toWrite = "";
            }
            else
            {
                string str = heardCall.ToString();
                if (str == null)
                {
                    toWrite = "";
                }
                else if (str.Length <= max)
                {
                    toWrite = str;
                }
                else
                {
                    toWrite = str.Substring(0, max);
                }
            }
            Console.Write(toWrite);
        }

        private static string GetGrid(byte[] msg)
        {
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

            if (split.Length == 2 || split.Length == 3 || split.Length == 4)
            {
                string candidate = split.Last();

                if (IsGrid(candidate))
                {
                    return candidate;
                }
                else
                {
                    if (Debugger.IsAttached && split.Any(s => IsGrid(s)))
                    {
                        Debugger.Break();
                    }

                    return null;
                }
            }
            else
            {
                if (Debugger.IsAttached && split.Any(s => IsGrid(s)))
                {
                    Debugger.Break();
                }

                return null;
            }
        }

        private static bool IsGrid(string v)
        {
            if (v == "RR73")
                return false;

            if (v == null || v.Length != 4)
                return false;

            if (!char.IsUpper(v[0]) || !char.IsUpper(v[1]))
                return false;

            if (!char.IsNumber(v[2]) || !char.IsNumber(v[3]))
                return false;

            return true;
        }

        private static string Pad(string heardCall, int v)
        {
            if (heardCall == null)
            {
                return new string(' ', v);
            }

            if (heardCall.Length >= v)
            {
                return heardCall.Substring(0, v);
            }

            return heardCall + new string(' ', v - heardCall.Length);
        }

        static string configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ft8spotter", "config");

        private static Dictionary<string, string> config;
        private static Dictionary<string, string> GetConfig()
        {
            if (config == null)
            {
                config = File.ReadAllLines(configFile)
                                .Where(line => line.Contains("="))
                                .Select(line => new { key = line.Substring(0, line.IndexOf("=")), value = line.Substring(line.IndexOf("=") + 1) })
                                .Where(kvp => !String.IsNullOrWhiteSpace(kvp.key) && !String.IsNullOrWhiteSpace(kvp.value))
                                .ToDictionary(line => line.key, line => line.value);
            }

            return config;
        }

        class Needed
        {
            public override bool Equals(object obj)
            {
                if (!(obj is Needed other))
                {
                    return false;
                }

                if (obj == null)
                    return false;

                return other.NewCountryOnBand == NewCountryOnBand
                    && other.NewCountryOnBandOnMode == NewCountryOnBandOnMode
                    && other.NewGridOnBand == NewGridOnBand
                    && other.NewGridOnBandOnMode == NewGridOnBandOnMode
                    && other.NewCountryOnAnyBand == NewCountryOnAnyBand
                    && other.NewGridOnAnyBand == NewGridOnAnyBand;
            }

            public override int GetHashCode()
            {
                return NewCountryOnBand.GetHashCode() ^ NewCountryOnBandOnMode.GetHashCode() ^ NewGridOnBand.GetHashCode()
                    ^ NewGridOnBandOnMode.GetHashCode() ^ NewCountryOnAnyBand.GetHashCode() ^ NewGridOnAnyBand.GetHashCode();
            }

            public bool NewCountryOnBandOnMode { get; set; }
            public bool NewCountryOnBand { get; set; }
            public bool NewGridOnBand { get; set; }
            public bool NewGridOnBandOnMode { get; set; }

            public static Needed No { get { return new Needed(); } }

            public bool NewCountryOnAnyBand { get; set; }
            public bool NewGridOnAnyBand { get; set; }

            public override string ToString()
            {
                if (NewCountryOnAnyBand)
                {
                    return "Country";
                }

                if (NewCountryOnBand)
                {
                    return "Country+Band";
                }

                if (NewCountryOnBandOnMode)
                {
                    return "Country+Band+Mode";
                }

                if (NewGridOnAnyBand)
                {
                    return "Grid";
                }

                if (NewGridOnBand)
                {
                    return "Grid+Band";
                }

                if (NewGridOnBandOnMode)
                {
                    return "Grid+Band+Mode";
                }

                return null;
            }
        }

        private static HttpClient httpClient = new HttpClient();
        private static Uri cloudLogUri;

        private static string HttpGet(string url)
        {
            int delay = 100;

            while (true)
            {
                try
                {
                    return httpClient.GetStringAsync(new Uri(cloudLogUri, url)).Result;
                }
                catch (Exception ex)
                {
                    string message;
                    if (ex is AggregateException aggregateException)
                    {
                        message = String.Join(Environment.NewLine, aggregateException.InnerExceptions.Select(e => e.Message));
                    }
                    else
                    {
                        message = ex.Message;
                    }

                    Console.WriteLine(message);
                    Thread.Sleep(delay);

                    if (delay < TimeSpan.FromMinutes(1).TotalMilliseconds)
                    {
                        delay *= 2;
                    }
                }
            }
        }

        private static Needed GetNeeded(int band, int? adif, string gridSquare, string mode)
        {
            if (!adif.HasValue)
            {
                return Needed.No;
            }

            int dxcc = adif.Value;

            int qsosWithThatCountryOnAnyBand = int.Parse(HttpGet($"country_worked/{dxcc}/all"));

            var result = new Needed();

            if (qsosWithThatCountryOnAnyBand == 0)
            {
                result.NewCountryOnAnyBand = true;
            }
            else
            {
                int qsosWithThatCountryOnCurrentBand = int.Parse(HttpGet($"country_worked/{dxcc}/{band}m"));

                if (qsosWithThatCountryOnCurrentBand == 0)
                {
                    result.NewCountryOnBand = true;
                }
                else
                {
                    int qsosWithThatCountryOnThatBandInThisMode = int.Parse(HttpGet($"country_worked/{dxcc}/{band}m/{mode}"));
                    if (qsosWithThatCountryOnThatBandInThisMode == 0)
                    {
                        result.NewCountryOnBandOnMode = true;
                    }
                }
            }

            if (!String.IsNullOrWhiteSpace(gridSquare) && gridSquare.Length >= 4)
            {
                if (gridSquare.Length > 4)
                {
                    gridSquare = gridSquare.Substring(0, 4);
                }

                int qsosWithThatGridOnAnyBand = int.Parse(HttpGet($"gridsquare_worked/{gridSquare}/all"));
                if (qsosWithThatGridOnAnyBand == 0)
                {
                    result.NewGridOnAnyBand = true;
                }
                else
                {
                    int qsosWithThatGridOnThatBand = int.Parse(HttpGet($"gridsquare_worked/{gridSquare}/{band}m"));
                    if (qsosWithThatGridOnThatBand == 0)
                    {
                        result.NewGridOnBand = true;
                    }
                    else
                    {
                        int qsosWithThatGridOnThatBandInThisMode = int.Parse(HttpGet($"gridsquare_worked/{gridSquare}/{band}m/{mode}"));
                        if (qsosWithThatGridOnThatBandInThisMode == 0)
                        {
                            result.NewGridOnBandOnMode = true;
                        }
                    }
                }
            }

            return result;
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

            return GetHeardCall(text);
        }
    }

    internal class MatchingEntity
    {
        public string Call { get; set; }
        public int? Adif { get; set; }
        public string Continent { get; set; }
        public int? CqZone { get; set; }
        private string _entity;
        public string Entity { get { return _entity; } set { _entity = Niceify(value); } }

        private string Niceify(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return string.Join(' ', value.Split(' ').Select(word => word == "OF" ? "of" : $"{word[0].ToString().ToUpper()}{(word.Length == 1 ? "" : new String(word.Skip(1).ToArray()).ToLower())}"));
        }

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
