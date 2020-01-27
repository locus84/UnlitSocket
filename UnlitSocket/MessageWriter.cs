using System;
using System.IO;

namespace UnlitSocket
{
    // Binary stream Writer. Supports simple types, buffers, arrays, structs, and nested types
    public static class MessageWriter
    {
        public static void WriteUInt16(this Message msg, ushort value)
        {
            msg.EnsureSize(2);
            msg.WriteByteNoCheck((byte)(value & 0xFF));
            msg.WriteByteNoCheck((byte)(value >> 8));
        }

        public static void WriteUInt16(byte[] bytes, ushort value)
        {
            if (bytes.Length < 2) throw new EndOfStreamException("WriteUInt16 out of range:" + bytes.Length);
            bytes[0] = (byte)(value & 0xFF);
            bytes[1] = (byte)(value >> 8);
        }

        public static void WriteUInt32(this Message msg, uint value)
        {
            msg.EnsureSize(4);
            msg.WriteByteNoCheck((byte)(value & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
        }

        public static void WriteUInt64(this Message msg, ulong value)
        {
            msg.EnsureSize(8);
            msg.WriteByteNoCheck((byte)(value & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 32) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 40) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 48) & 0xFF));
            msg.WriteByteNoCheck((byte)((value >> 56) & 0xFF));
        }

        public static void WriteSByte(this Message msg, sbyte value) => msg.WriteByte((byte)value);
        public static void WriteChar(this Message msg, char value) => msg.WriteUInt16((ushort)value);
        public static void WriteBoolean(this Message msg, bool value) => msg.WriteByte((byte)(value ? 1 : 0));
        public static void WriteInt16(this Message msg, short value) => msg.WriteUInt16((ushort)value);
        public static void WriteInt32(this Message msg, int value) => msg.WriteUInt32((uint)value);
        public static void WriteInt64(this Message msg, long value) => msg.WriteUInt64((ulong)value);
        public static void WriteSingle(this Message msg, float value) {
            UIntFloat converter = new UIntFloat
            {
                floatValue = value
            };
            msg.WriteUInt32(converter.intValue);
        }

        public static void WriteDouble(this Message msg, double value)
        {
            UIntDouble converter = new UIntDouble
            {
                doubleValue = value
            };
            msg.WriteUInt64(converter.longValue);
        }

        public static void WriteDecimal(this Message msg, decimal value)
        {
            // the only way to read it without allocations is to both read and
            // write it with the FloatConverter (which is not binary compatible
            // to writer.Write(decimal), hence why we use it here too)
            UIntDecimal converter = new UIntDecimal
            {
                decimalValue = value
            };
            msg.WriteUInt64(converter.longValue1);
            msg.WriteUInt64(converter.longValue2);
        }

        public static void WriteString(this Message msg, string value)
        {
            // write 0 for null support, increment real size by 1
            // (note: original HLAPI would write "" for null strings, but if a
            //        string is null on the server then it should also be null
            //        on the client)
            if (value == null)
            {
                msg.WriteUInt16((ushort)0);
                return;
            }

            // write string with same method as NetworkReader
            // convert to byte[]
            int size = Message.Encoding.GetBytes(value, 0, value.Length, Message.StringBuffer, 0);

            // check if within max size
            if (size >= Message.MAX_STRING_LENGTH)
            {
                throw new IndexOutOfRangeException("NetworkWriter.Write(string) too long: " + size + ". Limit: " + Message.MAX_STRING_LENGTH);
            }

            // write size and bytes
            msg.WriteUInt16(checked((ushort)(size + 1)));
            msg.WriteBytes(Message.StringBuffer, 0, size);
        }

        // zigzag encoding https://gist.github.com/mfuerstenau/ba870a29e16536fdbaba
        public static void WritePackedInt32(this Message msg, int i)
        {
            uint zigzagged = (uint)((i >> 31) ^ (i << 1));
            msg.WritePackedUInt32(zigzagged);
        }

        // http://sqlite.org/src4/doc/trunk/www/varint.wiki
        public static void WritePackedUInt32(this Message msg, uint value)
        {
            // for 32 bit values WritePackedUInt64 writes the
            // same exact thing bit by bit
            msg.WritePackedUInt64(value);
        }

        // zigzag encoding https://gist.github.com/mfuerstenau/ba870a29e16536fdbaba
        public static void WritePackedInt64(this Message msg, long i)
        {
            ulong zigzagged = (ulong)((i >> 63) ^ (i << 1));
            msg.WritePackedUInt64(zigzagged);
        }

        public static void WritePackedUInt64(this Message msg, ulong value)
        {
            if (value <= 240)
            {
                msg.WriteByte((byte)value);
                return;
            }
            if (value <= 2287)
            {
                msg.EnsureSize(2);
                msg.WriteByteNoCheck((byte)(((value - 240) >> 8) + 241));
                msg.WriteByteNoCheck((byte)((value - 240) & 0xFF));
                return;
            }
            if (value <= 67823)
            {
                msg.EnsureSize(3);
                msg.WriteByteNoCheck((byte)249);
                msg.WriteByteNoCheck((byte)((value - 2288) >> 8));
                msg.WriteByteNoCheck((byte)((value - 2288) & 0xFF));
                return;
            }
            if (value <= 16777215)
            {
                msg.EnsureSize(4);
                msg.WriteByteNoCheck((byte)250);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                return;
            }
            if (value <= 4294967295)
            {
                msg.EnsureSize(5);
                msg.WriteByteNoCheck((byte)251);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
                return;
            }
            if (value <= 1099511627775)
            {
                msg.EnsureSize(6);
                msg.WriteByteNoCheck((byte)252);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 32) & 0xFF));
                return;
            }
            if (value <= 281474976710655)
            {
                msg.EnsureSize(7);
                msg.WriteByteNoCheck((byte)253);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 32) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 40) & 0xFF));
                return;
            }
            if (value <= 72057594037927935)
            {
                msg.EnsureSize(8);
                msg.WriteByteNoCheck((byte)254);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 32) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 40) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 48) & 0xFF));
                return;
            }

            // all others
            {
                msg.EnsureSize(9);
                msg.WriteByteNoCheck((byte)255);
                msg.WriteByteNoCheck((byte)(value & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 8) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 16) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 24) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 32) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 40) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 48) & 0xFF));
                msg.WriteByteNoCheck((byte)((value >> 56) & 0xFF));
            }
        }

        //public static void WriteVector2(this Message msg, Vector2 value)
        //{
        //    msg.WriteSingle(value.x);
        //    msg.WriteSingle(value.y);
        //}

        //public static void WriteVector3(this Message msg, Vector3 value)
        //{
        //    msg.WriteSingle(value.x);
        //    msg.WriteSingle(value.y);
        //    msg.WriteSingle(value.z);
        //}

        //public static void WriteVector4(this Message msg, Vector4 value)
        //{
        //    msg.WriteSingle(value.x);
        //    msg.WriteSingle(value.y);
        //    msg.WriteSingle(value.z);
        //    msg.WriteSingle(value.w);
        //}

        //public static void WriteVector2Int(this Message msg, Vector2Int value)
        //{
        //    msg.WritePackedInt32(value.x);
        //    msg.WritePackedInt32(value.y);
        //}

        //public static void WriteVector3Int(this Message msg, Vector3Int value)
        //{
        //    msg.WritePackedInt32(value.x);
        //    msg.WritePackedInt32(value.y);
        //    msg.WritePackedInt32(value.z);
        //}

        //public static void WriteColor(this Message msg, Color value)
        //{
        //    msg.WriteSingle(value.r);
        //    msg.WriteSingle(value.g);
        //    msg.WriteSingle(value.b);
        //    msg.WriteSingle(value.a);
        //}

        //public static void WriteColor32(this Message msg, Color32 value)
        //{
        //    msg.EnsureSize(4);
        //    msg.WriteByteNoCheck(value.r);
        //    msg.WriteByteNoCheck(value.g);
        //    msg.WriteByteNoCheck(value.b);
        //    msg.WriteByteNoCheck(value.a);
        //}

        //public static void WriteQuaternion(this Message msg, Quaternion value)
        //{
        //    msg.WriteSingle(value.x);
        //    msg.WriteSingle(value.y);
        //    msg.WriteSingle(value.z);
        //    msg.WriteSingle(value.w);
        //}

        //public static void WriteRect(this Message msg, Rect value)
        //{
        //    msg.WriteSingle(value.xMin);
        //    msg.WriteSingle(value.yMin);
        //    msg.WriteSingle(value.width);
        //    msg.WriteSingle(value.height);
        //}

        //public static void WritePlane(this Message msg, Plane value)
        //{
        //    msg.WriteVector3(value.normal);
        //    msg.WriteSingle(value.distance);
        //}

        //public static void WriteRay(this Message msg, Ray value)
        //{
        //    msg.WriteVector3(value.origin);
        //    msg.WriteVector3(value.direction);
        //}

        //public static void WriteMatrix4x4(this Message msg, Matrix4x4 value)
        //{
        //    msg.WriteSingle(value.m00);
        //    msg.WriteSingle(value.m01);
        //    msg.WriteSingle(value.m02);
        //    msg.WriteSingle(value.m03);
        //    msg.WriteSingle(value.m10);
        //    msg.WriteSingle(value.m11);
        //    msg.WriteSingle(value.m12);
        //    msg.WriteSingle(value.m13);
        //    msg.WriteSingle(value.m20);
        //    msg.WriteSingle(value.m21);
        //    msg.WriteSingle(value.m22);
        //    msg.WriteSingle(value.m23);
        //    msg.WriteSingle(value.m30);
        //    msg.WriteSingle(value.m31);
        //    msg.WriteSingle(value.m32);
        //    msg.WriteSingle(value.m33);
        //}

        public static void WriteGuid(this Message msg, Guid value)
        {
            UIntGuid converter = new UIntGuid
            {
                guidValue = value
            };
            msg.WriteUInt64(converter.longValue1);
            msg.WriteUInt64(converter.longValue2);
        }
    }
}
