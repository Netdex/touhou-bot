using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EOSDBot
{
    public struct Vec2
    {
        public static Vec2 ZERO = new Vec2(0, 0);

        public double x { get; private set; }
        public double y { get; private set; }

        public Vec2(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public Vec2(Vec2 vec)
        {
            this.x = vec.x;
            this.y = vec.y;
        }

        public double Length()
        {
            return Math.Sqrt(LengthSq());
        }

        public double LengthSq()
        {
            return x * x + y * y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            a.x += b.x;
            a.y += b.y;
            return a;
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            a.x -= b.x;
            a.y -= b.y;
            return a;
        }

        public static Vec2 operator *(Vec2 a, double b)
        {
            a.x *= b;
            a.y *= b;
            return a;
        }

        public static Vec2 operator *(double b, Vec2 a)
        {
            a.x *= b;
            a.y *= b;
            return a;
        }

        public static Vec2 operator /(Vec2 a, double b)
        {
            a.x /= b;
            a.y /= b;
            return a;
        }

        public static Vec2 operator /(double b, Vec2 a)
        {
            a.x /= b;
            a.y /= b;
            return a;
        }

        public static bool operator ==(Vec2 a, Vec2 b)
        {
            return a.x == b.x && a.y == b.y;
        }

        public static bool operator !=(Vec2 a, Vec2 b)
        {
            return a.x != b.x || a.y != b.y;
        }

        public static double Dot(Vec2 a, Vec2 b)
        {
            return a.x * b.x + a.y * b.y;
        }

        public static double Cross(Vec2 a, Vec2 b)
        {
            return a.x*b.y - a.y*b.x;
        }

        public static Vec2 Cross(Vec2 a, double s)
        {
            a.x *= -s;
            a.y *= s;
            return a;
        }

        public static Vec2 Cross(double s, Vec2 a)
        {
            a.x *= s;
            a.y *= -s;
            return a;
        }

        public override string ToString()
        {
            return $"<{x}, {y}>";
        }
    }
}
