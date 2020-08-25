using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace ft8spotter
{
    public class ClublogCtyXml
    {
        //internal const string ClublogNamespace = "https://clublog.org/cty/v1.2";

        public DateTime Updated { get; set; }
        public Entity[] Entities { get; set; }
        public ExceptionRecord[] Exceptions { get; set; }
        public Prefix[] Prefixes { get; set; }
        public InvalidOperation[] InvalidOperations { get; set; }
        public ZoneException[] ZoneExceptions { get; set; }

        public static ClublogCtyXml Parse(string xml)
        {
            var result = new ClublogCtyXml();

            XDocument xDocument = XDocument.Parse(xml);

            result.Entities = Fetch(xDocument, "entities", "entity", (xe) => Entity.Parse(xe));
            result.Exceptions = Fetch(xDocument, "exceptions", "exception", (xe) => ExceptionRecord.Parse(xe));
            result.InvalidOperations = Fetch(xDocument, "invalid_operations", "invalid", (xe) => InvalidOperation.Parse(xe));
            result.Prefixes = Fetch(xDocument, "prefixes", "prefix", (xe) => Prefix.Parse(xe));
            result.ZoneExceptions = Fetch(xDocument, "zone_exceptions", "zone_exception", (xe) => ZoneException.Parse(xe));
            result.Updated = DateTime.Parse(xDocument.Root.Attribute("date").Value);

            return result;
        }

        static string GetNamespace(XDocument doc)
        {
            var ns = doc.Root.GetDefaultNamespace()?.NamespaceName;

            return ns;
        }

        private static T[] Fetch<T>(XDocument xDocument, string parent, string elementName, Func<XElement, T> parse) => xDocument.Element(XName.Get("clublog", GetNamespace(xDocument)))
                                                                                                        .Descendants(XName.Get(parent, GetNamespace(xDocument)))
                                                                                                        .Descendants(XName.Get(elementName, GetNamespace(xDocument)))
                                                                                                        .Select(parse)
                                                                                                        .ToArray();


        internal static bool? GetNullableBool(XElement xe, string v)
        {
            var s = GetString(xe, v);

            if (s == null)
            {
                return null;
            }

            if (s == "FALSE")
                return false;

            if (s == "TRUE")
                return true;

            throw new NotImplementedException(v);
        }

        internal static DateTime? GetNullableDateTime(XElement xe, string v)
        {
            var dt = GetString(xe, v);

            if (dt == null)
            {
                return null;
            }

            return DateTime.Parse(dt);
        }

        internal static bool GetBool(XElement xe, string v)
        {
            string s = GetString(xe, v);

            if (s == "FALSE")
                return false;

            if (s == "TRUE")
                return true;

            throw new NotImplementedException(v);
        }

        internal static string GetString(XElement xe, string elementName)
        {
            return xe.Element(XName.Get(elementName, GetNamespace(xe.Document)))?.Value;
        }

        internal static int? GetNullableInt(XElement xe, string v)
        {
            var s = GetString(xe, v);

            if (s == null)
            {
                return null;
            }

            return int.Parse(s);
        }

        internal static double? GetNullableDouble(XElement xe, string v)
        {
            var s = GetString(xe, v);

            if (s == null)
            {
                return null;
            }

            return double.Parse(s);
        }
    }

    public class ZoneException
    {
        public int Record { get; set; }
        public string Call { get; set; }
        public int Zone { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        internal static ZoneException Parse(XElement xe)
        {
            var result = new ZoneException
            {
                Record = int.Parse(xe.Attribute("record").Value),
                Call = ClublogCtyXml.GetString(xe, "call"),
                Zone = int.Parse(ClublogCtyXml.GetString(xe, "zone")),
                End = ClublogCtyXml.GetNullableDateTime(xe, "end"),
                Start = ClublogCtyXml.GetNullableDateTime(xe, "start"),
            };

            return result;
        }
    }

    public class InvalidOperation
    {
        public int Record { get; set; }
        public string Call { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        internal static InvalidOperation Parse(XElement xe)
        {
            var result = new InvalidOperation
            {
                Record = int.Parse(xe.Attribute("record").Value),
                Call = ClublogCtyXml.GetString(xe, "call"),
                End = ClublogCtyXml.GetNullableDateTime(xe, "end"),
                Start = ClublogCtyXml.GetNullableDateTime(xe, "start"),
            };

            return result;
        }
    }

    public class Entity
    {
        public int Adif { get; set; }
        public string Name { get; set; }
        public string Prefix { get; set; }
        public bool Deleted { get; set; }
        public int CqZone { get; set; }
        public string Continent { get; set; }
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public bool? Whitelist { get; set; }
        public DateTime? WhitelistStart { get; set; }
        public DateTime? WhitelistEnd { get; set; }

        internal static Entity Parse(XElement xe)
        {
            var result = new Entity
            {
                Adif = int.Parse(ClublogCtyXml.GetString(xe, "adif")),
                Continent = ClublogCtyXml.GetString(xe, "cont"),
                CqZone = int.Parse(ClublogCtyXml.GetString(xe, "cqz")),
                Deleted = ClublogCtyXml.GetBool(xe, "deleted"),
                End = ClublogCtyXml.GetNullableDateTime(xe, "end"),
                Start = ClublogCtyXml.GetNullableDateTime(xe, "start"),
                Latitude = double.Parse(ClublogCtyXml.GetString(xe, "lat")),
                Longitude = double.Parse(ClublogCtyXml.GetString(xe, "long")),
                Name = ClublogCtyXml.GetString(xe, "name"),
                Prefix = ClublogCtyXml.GetString(xe, "prefix"),
                Whitelist = ClublogCtyXml.GetNullableBool(xe, "whitelist"),
                WhitelistEnd = ClublogCtyXml.GetNullableDateTime(xe, "whitelist_end"),
                WhitelistStart = ClublogCtyXml.GetNullableDateTime(xe, "whitelist_start"),
            };

            return result;
        }
    }

    public class ExceptionRecord
    {
        public int Record { get; set; }
        public string Call { get; set; }
        public string Entity { get; set; }
        public int Adif { get; set; }
        public int CqZone { get; set; }
        public string Continent { get; set; }
        public double Longitude { get; set; }
        public double Latitude { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        internal static ExceptionRecord Parse(XElement xe)
        {
            return new ExceptionRecord
            {
                Record = int.Parse(xe.Attribute("record").Value),
                Call = ClublogCtyXml.GetString(xe, "call"),
                End = ClublogCtyXml.GetNullableDateTime(xe, "end"),
                Start = ClublogCtyXml.GetNullableDateTime(xe, "start"),
                Adif = int.Parse(ClublogCtyXml.GetString(xe, "adif")),
                Continent = ClublogCtyXml.GetString(xe, "cont"),
                CqZone = int.Parse(ClublogCtyXml.GetString(xe, "cqz")),
                Entity = ClublogCtyXml.GetString(xe, "entity"),
                Latitude = double.Parse(ClublogCtyXml.GetString(xe, "lat")),
                Longitude = double.Parse(ClublogCtyXml.GetString(xe, "long")),
            };
        }
    }

    public class Prefix
    {
        public int Record { get; set; }
        public string Call { get; set; }
        public string Entity { get; set; }
        public int? Adif { get; set; }
        public int? CqZone { get; set; }
        public string Continent { get; set; }
        public double? Longitude { get; set; }
        public double? Latitude { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }

        internal static Prefix Parse(XElement xe)
        {
            return new Prefix
            {
                Record = int.Parse(xe.Attribute("record").Value),
                Call = ClublogCtyXml.GetString(xe, "call"),
                End = ClublogCtyXml.GetNullableDateTime(xe, "end"),
                Start = ClublogCtyXml.GetNullableDateTime(xe, "start"),
                Adif = ClublogCtyXml.GetNullableInt(xe, "adif"),
                Continent = ClublogCtyXml.GetString(xe, "cont"),
                CqZone = ClublogCtyXml.GetNullableInt(xe, "cqz"),
                Entity = ClublogCtyXml.GetString(xe, "entity"),
                Latitude = ClublogCtyXml.GetNullableDouble(xe, "lat"),
                Longitude = ClublogCtyXml.GetNullableDouble(xe, "long"),
            };
        }
    }
}