using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Liechty.Alfalfa
{
    public class GeoLocation : IEquatable<GeoLocation>
    {
        // Rep Invariant: Either code must be != ulong.MaxValue or longitude and latitude must both be NOT NaN.

        public const double EarthRadiusInMeters = 6371000;
        public const double EarthRadiusInKilometers = EarthRadiusInMeters / 1000.0;
        public const double EarthRadiusInFeet = EarthRadiusInMeters / (0.0254 * 12.0);
        public const double EarthRadiusInMiles = EarthRadiusInFeet / 5280.0;

        public GeoLocation(ulong code)
        {
            this.Code = code;
        }

        public GeoLocation(double latitude, double longitude)
        {
            // A code of ulong.MaxValue is special and means "unspecified".
            // It would otherwise be one of the Gimbal-equivalent values of the North Pole.

            if (latitude > 90.0 || latitude < -90.0)
            {
                throw new ArgumentOutOfRangeException("latitude", "Latitudes greater than 90 degrees north or south are not supported.");
            }

            this.latitude = latitude;
            this.longitude = longitude;
        }

        public double RadiansFrom(GeoLocation other)
        {
            double dLat = Utilities.ToRadians(other.Latitude - this.Latitude);
            double dLon = Utilities.ToRadians(other.Longitude - this.Longitude);
            double sinDLatOverTwo = Math.Sin(dLat / 2.0);
            double sinDLonOverTwo = Math.Sin(dLon / 2.0);
            double a = sinDLatOverTwo * sinDLatOverTwo +
                Math.Cos(Utilities.ToRadians(this.Latitude)) * Math.Cos(Utilities.ToRadians(other.Latitude)) *
                sinDLonOverTwo * sinDLonOverTwo;
            double radians = 2.0 * Math.Asin(Math.Sqrt(a));
            return radians;
        }

        public double MetersFrom(GeoLocation other)
        {
            return this.RadiansFrom(other) * EarthRadiusInMeters;
        }

        public double MilesFrom(GeoLocation other)
        {
            return this.RadiansFrom(other) * EarthRadiusInMiles;
        }

        private ulong code = ulong.MaxValue;
        public ulong Code
        {
            get
            {
                if (this.code == ulong.MaxValue)
                {
                    ulong value = 0;
                    double longitudeLeft = this.longitude + 180.0;
                    double latitudeLeft = this.latitude + 90.0;
                    double magnitude = 180.0;
                    ulong bitMask = 0x8000000000000000;

                    while (bitMask != 0)
                    {
                        if (longitudeLeft >= magnitude)
                        {
                            value |= bitMask;
                            longitudeLeft -= magnitude;
                        }

                        magnitude = magnitude / 2.0;
                        bitMask = bitMask >> 1;
                        if (latitudeLeft >= magnitude)
                        {
                            value |= bitMask;
                            latitudeLeft -= magnitude;
                        }

                        bitMask = bitMask >> 1;
                    }

                    if (value == ulong.MaxValue) value -= 1;
                    this.code = value;
                }

                return this.code;
            }

            set
            {
                if (value == ulong.MaxValue)
                {
                    value -= 1;
                }

                this.code = value;
                this.latitude = double.NaN;
                this.longitude = double.NaN;
            }
        }

        private double longitude;
        public double Longitude
        {
            get
            {
                if (double.IsNaN(this.longitude))
                {
                    ulong bitMask = 0x8000000000000000;
                    double magnitude = 180.0;
                    double value = 0;
                    while (bitMask != 0)
                    {
                        if ((this.code & bitMask) != 0)
                        {
                            value += magnitude;
                        }

                        bitMask = bitMask >> 2;
                        magnitude = magnitude / 2.0;
                    }

                    this.longitude = value - 180.0; // Shift interval from [0,180) to [-90,90)
                }

                return this.longitude;
            }
        }

        private double latitude;
        public double Latitude
        {
            get
            {
                if (double.IsNaN(this.latitude))
                {
                    ulong bitMask = 0x4000000000000000;
                    double magnitude = 90.0;
                    double value = 0;
                    while (bitMask != 0)
                    {
                        if ((this.code & bitMask) != 0)
                        {
                            value += magnitude;
                        }

                        bitMask = bitMask >> 2;
                        magnitude = magnitude / 2;
                    }

                    this.latitude = value - 90.0; // Shift interval from [0,180) to [-90,90)
                }

                return this.latitude;
            }
        }

        public override string ToString()
        {
            return String.Format("({0}, {1})", this.Latitude, this.Longitude);
        }

        public bool Equals(GeoLocation other)
        {
            return this.Code == other.Code;
        }
    }

    internal class GeoCircle
    {
        public GeoCircle(GeoLocation center, double radiusInMeters)
        {
            this.Center = center;
            this.RadiusInMeters = radiusInMeters;
            double latitude = center.Latitude;
            double longitude = center.Longitude;
            double longitudeAway = Utilities.MetersToLongitudeDifference(radiusInMeters, latitude);
            double latitudeAway = Utilities.ToDegrees(radiusInMeters / GeoLocation.EarthRadiusInMeters);
            this.MinLatitude = latitude - latitudeAway;
            this.MaxLatitude = latitude + latitudeAway;
            this.MinLongitude = longitude - longitudeAway;
            this.MaxLongitude = longitude + longitudeAway;
        }

        public GeoLocation Center { get; private set; }
        public double RadiusInMeters { get; private set; }
        public double MinLatitude { get; private set; }
        public double MaxLatitude { get; private set; }
        public double MinLongitude { get; private set; }
        public double MaxLongitude { get; private set; }
    }

    internal struct GeoRect
    {
        public GeoLocation Southwest { get; set; }
        public GeoLocation Northeast { get; set; }

        public override string ToString()
        {
            return String.Format("({0}, {1})", this.Northeast, this.Southwest);
        }

        public bool Contains(GeoLocation location)
        {
            bool containsPoint =
                location.Latitude >= this.Southwest.Latitude &&
                location.Latitude <= this.Northeast.Latitude &&
                location.Longitude >= this.Southwest.Longitude &&
                location.Longitude <= this.Northeast.Longitude;

            return containsPoint;
        }

        private bool AnySidesIntersectAnyCardinalRadii(GeoCircle circle)
        {
            bool longitudeSidesIntersectCardinalAxesOfCircle =
                (circle.Center.Longitude >= this.Southwest.Longitude && circle.Center.Longitude <= this.Northeast.Longitude);
            bool latitudeSidesIntersectCardinalAxesOfCircle =
                (circle.Center.Latitude >= this.Southwest.Latitude && circle.Center.Latitude <= this.Northeast.Latitude);

            bool anySidesIntersectAnyCardinalRadii =
                (latitudeSidesIntersectCardinalAxesOfCircle &&
                    (this.Southwest.Latitude >= circle.MinLatitude && this.Southwest.Latitude <= circle.MaxLatitude) ||
                    (this.Northeast.Latitude >= circle.MinLatitude && this.Northeast.Latitude <= circle.MaxLatitude)) ||
                (longitudeSidesIntersectCardinalAxesOfCircle &&
                    (this.Southwest.Longitude >= circle.MinLongitude && this.Southwest.Longitude <= circle.MaxLongitude) ||
                    (this.Northeast.Longitude >= circle.MinLongitude && this.Northeast.Longitude <= circle.MaxLongitude));

            return anySidesIntersectAnyCardinalRadii;
        }

        private bool AnyCornerInsideCircle(GeoCircle circle)
        {
            bool anyCornerInsideCircle =
                circle.Center.MetersFrom(this.Southwest) <= circle.RadiusInMeters ||
                circle.Center.MetersFrom(this.Northeast) <= circle.RadiusInMeters ||
                circle.Center.MetersFrom(new GeoLocation(this.Southwest.Latitude, this.Northeast.Longitude)) <= circle.RadiusInMeters ||
                circle.Center.MetersFrom(new GeoLocation(this.Northeast.Latitude, this.Southwest.Longitude)) <= circle.RadiusInMeters;

            return anyCornerInsideCircle;
        }

        public bool OverlapsCircle(GeoCircle geoCircle)
        {
            bool overlapsCircle =
                this.Contains(geoCircle.Center) ||
                this.AnySidesIntersectAnyCardinalRadii(geoCircle) ||
                this.AnyCornerInsideCircle(geoCircle);

            return overlapsCircle;
        }
    }
}
