using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Liechty.Alfalfa
{
    internal static class Utilities
    {
        public static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        public static double ToDegrees(double radians)
        {
            return radians / Math.PI * 180.0;
        }

        public static double MetersToLongitudeDifference(double meters, double latitude)
        {
            if (latitude > 90.0 || latitude < -90.0)
            {
                throw new ArgumentOutOfRangeException("latitude");
            }

            double latitudeRingRadiusInMeters = Math.Cos(ToRadians(latitude)) * GeoLocation.EarthRadiusInMeters;
            double longitudeDifference = ToDegrees(meters / latitudeRingRadiusInMeters);
            return longitudeDifference;
        }
    }
}
