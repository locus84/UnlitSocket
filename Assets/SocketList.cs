using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;

public class SocketArgsList : IList
{
    List<SocketAsyncEventArgs> m_InnserList = new List<SocketAsyncEventArgs>();
    SocketIEnumerator m_InnerIEnumerator = new SocketIEnumerator();

    public object this[int index]
    {
        get
        {
            return m_InnserList[index].AcceptSocket;
        }

        set => m_InnserList[index].AcceptSocket = (Socket)value;
    }

    public SocketAsyncEventArgs ArgsAtIndex(int index) => m_InnserList[index];

    public bool IsFixedSize => throw new NotImplementedException();

    public bool IsReadOnly => throw new NotImplementedException();

    public int Count => m_InnserList.Count;

    public bool IsSynchronized => throw new NotImplementedException();

    public object SyncRoot => throw new NotImplementedException();

    public SocketArgsList()
    {
        m_InnerIEnumerator.m_ListRef = m_InnserList;
    }

    public void AddRange(IEnumerable<SocketAsyncEventArgs> args)
    {
        m_InnserList.AddRange(args);
    }

    public int Add(object value)
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        m_InnserList.Clear();
        m_InnerIEnumerator.Reset();
    }

    public bool Contains(object value)
    {
        throw new NotImplementedException();
    }

    public void CopyTo(Array array, int index)
    {
        throw new NotImplementedException();
    }

    public IEnumerator GetEnumerator()
    {
        return m_InnerIEnumerator;
    }

    public int IndexOf(object value)
    {
        throw new NotImplementedException();
    }

    public void Insert(int index, object value)
    {
        throw new NotImplementedException();
    }

    public void Remove(object value)
    {
        throw new NotImplementedException();
    }

    public void RemoveAt(int index)
    {
        m_InnserList.RemoveAt(index);
    }

    public class SocketIEnumerator : IEnumerator
    {
        public List<SocketAsyncEventArgs> m_ListRef;
        public int CurrentIndex = -1;

        public object Current => m_ListRef[CurrentIndex].AcceptSocket;

        public bool MoveNext()
        {
            return ++CurrentIndex < m_ListRef.Count;
        }

        public void Reset()
        {
            CurrentIndex = -1;
        }
    }
}
