﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace UnlitSocket
{
    public class Message
    {
        const int MAX_BYTE_ARRAY_SIZE = 256;

        static ConcurrentQueue<byte[]> s_ByteArrayPool = new ConcurrentQueue<byte[]>();
        static ConcurrentQueue<Message> s_MessagePool = new ConcurrentQueue<Message>();

        internal const int MAX_STRING_LENGTH = 1024 * 32;
        internal static readonly UTF8Encoding Encoding = new UTF8Encoding(false, true);
        internal static readonly byte[] StringBuffer = new byte[MAX_STRING_LENGTH];

        public int Capacity => MAX_BYTE_ARRAY_SIZE * m_InnerDatas.Count;
        private byte[] m_SendSize = new byte[2];
        private List<byte[]> m_InnerDatas = new List<byte[]>();
        public int Position = 0;
        public int Size { get; internal set; }
        private int m_RefCount = 0;

        public static void WarmUpMessage(int count)
        {
            while(s_MessagePool.Count < count)
                s_MessagePool.Enqueue(new Message());
        }

        private Message()
        {
            m_InnerDatas.Add(new byte[MAX_BYTE_ARRAY_SIZE]);
        }

        private ref byte ByteAtIndex(int position)
        {
            return ref m_InnerDatas[Math.DivRem(position, MAX_BYTE_ARRAY_SIZE, out var remainder)][remainder];
        }

        public void CheckSize(int count)
        {
            if (Position + count > Capacity || Position + count > ushort.MaxValue)
            {
                throw new EndOfStreamException("CheckSize out of range:" + ToString());
            }
        }

        public void EnsureSize(int amount)
        {
            if (Position + amount > ushort.MaxValue)
            {
                throw new EndOfStreamException("EnsureSize out of range:" + ToString());
            }

            while (Position + amount >= MAX_BYTE_ARRAY_SIZE * m_InnerDatas.Count)
            {
                byte[] newByteArray;
                if (!s_ByteArrayPool.TryDequeue(out newByteArray))
                {
                    newByteArray = new byte[MAX_BYTE_ARRAY_SIZE];
                }
                m_InnerDatas.Add(newByteArray);
            }
        }

        public byte ReadByte()
        {
            CheckSize(1);
            return ByteAtIndex(Position++);
        }

        public byte ReadByteNoCheck()
        {
            return ByteAtIndex(Position++);
        }

        public void ReadBytes(byte[] bytes, int offset, int count)
        {
            // check if passed byte array is big enough
            if (count > bytes.Length - offset)
            {
                throw new EndOfStreamException("ReadBytes can't read " + count + " + bytes because the passed byte[] only has length " + bytes.Length);
            }

            CheckSize(count);
            var bytesLeft = count;

            while (bytesLeft > 0)
            {
                //get innerdata Index and remainder
                var quotient = Math.DivRem(Position, MAX_BYTE_ARRAY_SIZE, out var remainder);
                //count we can read in this data arry or bytes left.
                var readCount = Math.Min(MAX_BYTE_ARRAY_SIZE - remainder, bytesLeft);
                //copy buffer to provided bytes.
                Buffer.BlockCopy(m_InnerDatas[quotient], remainder, bytes, offset + count - bytesLeft, readCount);
                //append howmuch we wrote
                bytesLeft -= readCount;
                Position += readCount;
            }
        }

        public void ReadSegment(ArraySegment<byte> segment)
        {
            ReadBytes(segment.Array, segment.Offset, segment.Count);
        }

        public void WriteByte(byte value)
        {
            EnsureSize(1);
            ByteAtIndex(Position++) = value;
            Size = Position;
        }

        public void WriteByteNoCheck(byte value)
        {
            ByteAtIndex(Position++) = value;
            Size = Position;
        }

        public void WriteBytes(byte[] bytes, int offset, int count)
        {
            // check if passed byte array is big enough
            if (count > bytes.Length - offset)
            {
                throw new EndOfStreamException("WriteBytes can't write " + count + " + bytes because the passed byte[] only has length " + bytes.Length);
            }

            EnsureSize(count);
            var bytesLeft = count;

            while (bytesLeft > 0)
            {
                //get innerdata Index and remainder
                var quotient = Math.DivRem(Position, MAX_BYTE_ARRAY_SIZE, out var remainder);
                //count we can write in this data arry or bytes left.
                var writeCount = Math.Min(MAX_BYTE_ARRAY_SIZE - remainder, bytesLeft);
                //copy provided bytes to buffer.
                Buffer.BlockCopy(bytes, offset + count - bytesLeft, m_InnerDatas[quotient], remainder, writeCount);
                //append howmuch we wrote
                bytesLeft -= writeCount;
                Position += writeCount;
            }

            Size = Position;
        }

        public void WriteSegment(ArraySegment<byte> segment)
        {
            WriteBytes(segment.Array, segment.Offset, segment.Count);
        }
        
        public void WriteMessage(Message msg)
        {
            //empty message
            if (msg.Position == 0) return;

            var countLeft = msg.Position;
            for(int i = 0; i < msg.m_InnerDatas.Count; i++)
            {
                var data = msg.m_InnerDatas[i];
                if (data.Length > countLeft)
                {
                    WriteBytes(data, 0, data.Length);
                    countLeft -= data.Length;
                }
                else
                {
                    WriteBytes(data, 0, countLeft);
                    break;
                }
            }
        }


        public void Clear()
        {
            //keep only one, discard rest
            while(m_InnerDatas.Count > 1)
            {
                var poolToReturn = m_InnerDatas[m_InnerDatas.Count - 1];
                s_ByteArrayPool.Enqueue(poolToReturn);
                m_InnerDatas.RemoveAt(m_InnerDatas.Count - 1);
            }
            Position = 0;
        }

        public void Retain()
        {
            Interlocked.Increment(ref m_RefCount);
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref m_RefCount) == 0)
            {
                Clear();
                s_MessagePool.Enqueue(this);
            }
        }

        public static Message Pop()
        {
            Message result;
            if (!s_MessagePool.TryDequeue(out result))
            {
                result = new Message();
            }
            result.Retain();
            return result;
        }

        internal void BindToArgsReceive(SocketAsyncEventArgs args, int count)
        {
            EnsureSize(count);
            args.BufferList.Clear();

            //encode actual data
            var quotient = Math.DivRem(count, MAX_BYTE_ARRAY_SIZE, out var remainder);
            for (int i = 0; i < quotient; i++)
                args.BufferList.Add(new ArraySegment<byte>(m_InnerDatas[i]));
            if (remainder > 0) args.BufferList.Add(new ArraySegment<byte>(m_InnerDatas[quotient], 0, remainder));

            //refresh bufferlist
            args.BufferList = args.BufferList;
        }

        internal void BindToArgsSend(SocketAsyncEventArgs args)
        {
            args.BufferList.Clear();

            //encode size
            MessageWriter.WriteUInt16(m_SendSize, (ushort)Position);
            args.BufferList.Add(new ArraySegment<byte>(m_SendSize));

            //encode actual data
            var quotient = Math.DivRem(Position, MAX_BYTE_ARRAY_SIZE, out var remainder);
            for (int i = 0; i < quotient; i++)
                args.BufferList.Add(new ArraySegment<byte>(m_InnerDatas[i]));
            if (remainder > 0) args.BufferList.Add(new ArraySegment<byte>(m_InnerDatas[quotient], 0, remainder));

            //refresh bufferlist
            args.BufferList = args.BufferList;
        }

        internal static void AdvanceRecevedOffset(SocketAsyncEventArgs args, int initialBufferCount, int readCount)
        {
            var quotient = Math.DivRem(readCount, MAX_BYTE_ARRAY_SIZE, out var remainder);
            while (args.BufferList.Count > initialBufferCount - quotient)
                args.BufferList.RemoveAt(0);

            if (remainder > 0)
            {
                var prevSegment = args.BufferList[0];
                var countLeft = prevSegment.Count - (remainder - prevSegment.Offset);
                args.BufferList[0] = new ArraySegment<byte>(prevSegment.Array, remainder, countLeft);
            }

            args.BufferList = args.BufferList;
        }
    }
}
