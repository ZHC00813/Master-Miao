using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Blankedge.SWHoleTools.Utilities
{
    public struct Vector3
    {
        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public Vector3 Normalized()
        {
            double length = Length;
            return length <= SwMath.GeometryTolerance ? default(Vector3) : this / length;
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vector3 operator *(Vector3 a, double scale) => new Vector3(a.X * scale, a.Y * scale, a.Z * scale);
        public static Vector3 operator /(Vector3 a, double scale) => new Vector3(a.X / scale, a.Y / scale, a.Z / scale);
        public static double Dot(Vector3 a, Vector3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        public double[] ToArray() => new[] { X, Y, Z };
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:F6}, {1:F6}, {2:F6})", X, Y, Z);
    }

    public static class SwMath
    {
        public const double GeometryTolerance = 1e-7;
        public const double AxisAngularTolerance = 1e-5;

        public static double ProjectStation(Vector3 point, Vector3 origin, Vector3 axis) =>
            Vector3.Dot(point - origin, axis.Normalized());

        public static Vector3 PointAt(Vector3 origin, Vector3 axis, double station) =>
            origin + (axis.Normalized() * station);

        public static double Distance(Vector3 a, Vector3 b) => (a - b).Length;

        public static double DistancePointToLine(Vector3 point, Vector3 lineOrigin, Vector3 lineDirection)
        {
            Vector3 unit = lineDirection.Normalized();
            return Vector3.Cross(point - lineOrigin, unit).Length;
        }

        public static bool AreParallel(Vector3 first, Vector3 second, double tolerance = AxisAngularTolerance)
        {
            Vector3 a = first.Normalized();
            Vector3 b = second.Normalized();
            return Math.Abs(Math.Abs(Vector3.Dot(a, b)) - 1.0) <= tolerance;
        }

        public static double[] ToDoubleArray(object value)
        {
            if (value == null)
            {
                return Array.Empty<double>();
            }

            if (value is double[] doubles)
            {
                return doubles;
            }

            if (!(value is Array array))
            {
                return Array.Empty<double>();
            }

            var result = new double[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                result[i] = Convert.ToDouble(array.GetValue(i), CultureInfo.InvariantCulture);
            }

            return result;
        }

        public static IList<T> ToComList<T>(object value) where T : class
        {
            var result = new List<T>();
            if (value is Array array)
            {
                foreach (object item in array)
                {
                    if (item is T typed)
                    {
                        result.Add(typed);
                    }
                }
            }
            else if (value is T single)
            {
                result.Add(single);
            }

            return result;
        }

        public static bool TryReadPoint(object value, out Vector3 point)
        {
            double[] data = ToDoubleArray(value);
            if (data.Length >= 3)
            {
                point = new Vector3(data[0], data[1], data[2]);
                return true;
            }

            point = default(Vector3);
            return false;
        }

        public static long GetComIdentity(object value)
        {
            if (value == null)
            {
                return 0;
            }

            IntPtr unknown = IntPtr.Zero;
            try
            {
                unknown = Marshal.GetIUnknownForObject(value);
                return unknown.ToInt64();
            }
            catch
            {
                return value.GetHashCode();
            }
            finally
            {
                if (unknown != IntPtr.Zero)
                {
                    Marshal.Release(unknown);
                }
            }
        }
    }
}
