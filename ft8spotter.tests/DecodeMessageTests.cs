﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace ft8spotter.tests
{
    public class DecodeMessageTests
    {
        // 081700 -12  0.3  367 ~  EC1AIJ US2YW KN28
        string testData = @"ad bc cb da 00 00 00 02
00 00 00 02 00 00 00 06
57 53 4a 54 2d 58 01 01
c7 04 60 ff ff ff f4 3f
d3 33 33 40 00 00 00 00
00 01 6f 00 00 00 01 7e
00 00 00 11 45 43 31 41
49 4a 20 55 53 32 59 57
20 4b 4e 32 38 00 00";

        /*
         * ad bc cb da = magic number
         * 00 00 00 02 = schema version
         * 00 00 00 02 = message type 2 (decode)
         * 00 00 00 06 = Id field: next 6 bytes are a string
         * 57 53 4a 54 2d 58 = Id field: WSJT-X
         * 01 = new field
         * 01 c7 04 60 = 29,820,000ms since midnight
         * ff ff ff f4 = snr -12 (big endian int32 - https://www.scadacore.com/tools/programming-calculators/online-hex-converter/)
         * 3f d3 33 33 40 00 00 00 - delta time (double)
         * 00 00 01 6f - delta frequency: 367
         * 00 00 00 01 = mode field: next 1 byte is a string
         * 7e = mode field = ~
         * 00 00 00 11 = message field: next 0x11 / 17 bytes are a string
         * 45 43 31 41 49 4a 20 55 53 32 59 57 20 4b 4e 32 38 = EC1AIJ US2YW KN28
         * 00 = low confidence
         * 00 = off air
         */

        IEnumerable<byte> message
        {
            get
            {
                foreach (string line in testData.Split(Environment.NewLine))
                {
                    foreach (string hexString in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        yield return byte.Parse(hexString, NumberStyles.HexNumber);
                    }
                }
            }
        }

        [Fact]
        public void SchemaVersion()
        {
            var data = message.ToArray();

            Assert.Equal(ParseResult.Success, DecodeMessage.TryParse(data, out DecodeMessage decodeMessage));

            Assert.Equal(2, decodeMessage.SchemaVersion);
            Assert.Equal("WSJT-X", decodeMessage.Id);
            Assert.True(decodeMessage.New);
            Assert.Equal(TimeSpan.FromSeconds(29820), decodeMessage.SinceMidnight);
            Assert.Equal(-12, decodeMessage.Snr);
            Assert.Equal(0.3, Math.Round(decodeMessage.DeltaTime, 1));
            Assert.Equal(367, decodeMessage.DeltaFrequency);
            Assert.Equal("~", decodeMessage.Mode);
            Assert.Equal("EC1AIJ US2YW KN28", decodeMessage.Message);
            Assert.False(decodeMessage.LowConfidence);
            Assert.False(decodeMessage.OffAir);
        }
    }
}