using System;
using System.IO;

namespace UnlitSocket
{
    public static class MessageReader
    {
        public static sbyte ReadSByte(this Message msg) => (sbyte)msg.ReadByte();
        public static char ReadChar(this Message msg) => (char)msg.ReadUInt16();
        public static bool ReadBoolean(this Message msg) => msg.ReadByte() != 0;
        public static short ReadInt16(this Message msg) => (short)msg.ReadUInt16();
        public static ushort ReadUInt16(this Message msg)
        {
            msg.CheckSize(2);
            ushort value = 0;
            value |= msg.ReadByteNoCheck();
            value |= (ushort)(msg.ReadByteNoCheck() << 8);
            return value;
        }

        public static ushort ReadUInt16(byte[] bytes)
        {
            if(bytes.Length < 2) throw new EndOfStreamException("ReadUInt16 out of range:" + bytes.Length);
            ushort value = 0;
            value |= bytes[0];
            value |= (ushort)(bytes[1] << 8);
            return value;
        }

        public static int ReadInt32(this Message msg) => (int)msg.ReadUInt32();
        public static uint ReadUInt32(this Message msg)
        {
            msg.CheckSize(4);
            uint value = 0;
            value |= msg.ReadByteNoCheck();
            value |= (uint)(msg.ReadByteNoCheck() << 8);
            value |= (uint)(msg.ReadByteNoCheck() << 16);
            value |= (uint)(msg.ReadByteNoCheck() << 24);
            return value;
        }

        public static long ReadInt64(this Message msg) => (long)msg.ReadUInt64();
        public static ulong ReadUInt64(this Message msg)
        {
            msg.CheckSize(8);
            ulong value = 0;
            value |= msg.ReadByteNoCheck();
            value |= ((ulong)msg.ReadByteNoCheck()) << 8;
            value |= ((ulong)msg.ReadByteNoCheck()) << 16;
            value |= ((ulong)msg.ReadByteNoCheck()) << 24;
            value |= ((ulong)msg.ReadByteNoCheck()) << 32;
            value |= ((ulong)msg.ReadByteNoCheck()) << 40;
            value |= ((ulong)msg.ReadByteNoCheck()) << 48;
            value |= ((ulong)msg.ReadByteNoCheck()) << 56;
            return value;
        }
        public static float ReadSingle(this Message msg)
        {
            UIntFloat converter = new UIntFloat();
            converter.intValue = msg.ReadUInt32();
            return converter.floatValue;
        }
        public static double ReadDouble(this Message msg)
        {
            UIntDouble converter = new UIntDouble();
            converter.longValue = msg.ReadUInt64();
            return converter.doubleValue;
        }
        public static decimal ReadDecimal(this Message msg)
        {
            UIntDecimal converter = new UIntDecimal();
            converter.longValue1 = msg.ReadUInt64();
            converter.longValue2 = msg.ReadUInt64();
            return converter.decimalValue;
        }

        // note: this will throw an ArgumentException if an invalid utf8 string is sent
        // null support, see NetworkWriter
        public static string ReadString(this Message msg)
        {
            // read number of bytes
            ushort size = msg.ReadUInt16();

            if (size == 0)
                return null;

            //treat zero as null, so real size is size - 1
            int realSize = size - 1;

            // make sure it's within limits to avoid allocation attacks etc.
            if (realSize >= Message.MAX_STRING_LENGTH)
            {
                throw new EndOfStreamException("ReadString too long: " + realSize + ". Limit is: " + Message.MAX_STRING_LENGTH);
            }

            // convert directly from buffer to string via encoding
            msg.ReadBytes(Message.StringBuffer, 0, realSize);

            string result = Message.Encoding.GetString(Message.StringBuffer, 0, realSize);
            return result;
        }

        // zigzag decoding https://gist.github.com/mfuerstenau/ba870a29e16536fdbaba
        public static int ReadPackedInt32(this Message msg)
        {
            uint data = msg.ReadPackedUInt32();
            return (int)((data >> 1) ^ -(data & 1));
        }

        // http://sqlite.org/src4/doc/trunk/www/varint.wiki
        // NOTE: big endian.
        // Use checked() to force it to throw OverflowException if data is invalid
        public static uint ReadPackedUInt32(this Message msg) => checked((uint)msg.ReadPackedUInt64());

        // zigzag decoding https://gist.github.com/mfuerstenau/ba870a29e16536fdbaba
        public static long ReadPackedInt64(this Message msg)
        {
            ulong data = msg.ReadPackedUInt64();
            return ((long)(data >> 1)) ^ -((long)data & 1);
        }

        public static ulong ReadPackedUInt64(this Message msg)
        {
            byte a0 = msg.ReadByte();
            if (a0 < 241)
            {
                return a0;
            }

            byte a1 = msg.ReadByte();
            if (a0 >= 241 && a0 <= 248)
            {
                return 240 + ((a0 - (ulong)241) << 8) + a1;
            }

            byte a2 = msg.ReadByte();
            if (a0 == 249)
            {
                return 2288 + ((ulong)a1 << 8) + a2;
            }

            byte a3 = msg.ReadByte();
            if (a0 == 250)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16);
            }

            byte a4 = msg.ReadByte();
            if (a0 == 251)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16) + (((ulong)a4) << 24);
            }

            byte a5 = msg.ReadByte();
            if (a0 == 252)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16) + (((ulong)a4) << 24) + (((ulong)a5) << 32);
            }

            byte a6 = msg.ReadByte();
            if (a0 == 253)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16) + (((ulong)a4) << 24) + (((ulong)a5) << 32) + (((ulong)a6) << 40);
            }

            byte a7 = msg.ReadByte();
            if (a0 == 254)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16) + (((ulong)a4) << 24) + (((ulong)a5) << 32) + (((ulong)a6) << 40) + (((ulong)a7) << 48);
            }

            byte a8 = msg.ReadByte();
            if (a0 == 255)
            {
                return a1 + (((ulong)a2) << 8) + (((ulong)a3) << 16) + (((ulong)a4) << 24) + (((ulong)a5) << 32) + (((ulong)a6) << 40) + (((ulong)a7) << 48)  + (((ulong)a8) << 56);
            }

            throw new IndexOutOfRangeException("ReadPackedUInt64() failure: " + a0);
        }

        //public static Vector2 ReadVector2(this Message msg) => new Vector2(msg.ReadSingle(), msg.ReadSingle());
        //public static Vector3 ReadVector3(this Message msg) => new Vector3(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
        //public static Vector4 ReadVector4(this Message msg) => new Vector4(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
        //public static Vector2Int ReadVector2Int(this Message msg) => new Vector2Int(msg.ReadPackedInt32(), msg.ReadPackedInt32());
        //public static Vector3Int ReadVector3Int(this Message msg) => new Vector3Int(msg.ReadPackedInt32(), msg.ReadPackedInt32(), msg.ReadPackedInt32());
        //public static Color ReadColor(this Message msg) => new Color(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
        //public static Color32 ReadColor32(this Message msg) => new Color32(msg.ReadByte(), msg.ReadByte(), msg.ReadByte(), msg.ReadByte());
        //public static Quaternion ReadQuaternion(this Message msg) => new Quaternion(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
        //public static Rect ReadRect(this Message msg) => new Rect(msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle(), msg.ReadSingle());
        //public static Plane ReadPlane(this Message msg) => new Plane(msg.ReadVector3(), msg.ReadSingle());
        //public static Ray ReadRay(this Message msg) => new Ray(msg.ReadVector3(), msg.ReadVector3());

        //public static Matrix4x4 ReadMatrix4x4(this Message msg)
        //{
        //    return new Matrix4x4
        //    {
        //        m00 = msg.ReadSingle(),
        //        m01 = msg.ReadSingle(),
        //        m02 = msg.ReadSingle(),
        //        m03 = msg.ReadSingle(),
        //        m10 = msg.ReadSingle(),
        //        m11 = msg.ReadSingle(),
        //        m12 = msg.ReadSingle(),
        //        m13 = msg.ReadSingle(),
        //        m20 = msg.ReadSingle(),
        //        m21 = msg.ReadSingle(),
        //        m22 = msg.ReadSingle(),
        //        m23 = msg.ReadSingle(),
        //        m30 = msg.ReadSingle(),
        //        m31 = msg.ReadSingle(),
        //        m32 = msg.ReadSingle(),
        //        m33 = msg.ReadSingle()
        //    };
        //}

        public static Guid ReadGuid(this Message msg)
        {
            UIntGuid converter = new UIntGuid();
            converter.longValue1 = msg.ReadUInt64();
            converter.longValue2 = msg.ReadUInt64();
            return converter.guidValue;
        } 
    }

}
