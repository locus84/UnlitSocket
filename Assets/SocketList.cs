using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEngine;

public class SocketList : IList
{
    List<Socket> m_InnserList;
    SocketIEnumerator m_InnerIEnumerator = new SocketIEnumerator();

    public object this[int index] { get
        {
            Debug.Log($"Get At {index}");
            return m_InnserList[index];
        }
            
        set => m_InnserList[index] = (Socket)value; }

    public bool IsFixedSize => throw new NotImplementedException();

    public bool IsReadOnly => throw new NotImplementedException();

    public int Count => m_InnserList.Count;

    public bool IsSynchronized => throw new NotImplementedException();

    public object SyncRoot => throw new NotImplementedException();

    public void SetInnerList(List<Socket> list)
    {
        m_InnserList = list;
        m_InnerIEnumerator.m_ListRef = list;
    }

    public int Add(object value)
    {
        UnityEngine.Debug.Log("Added");
        m_InnserList.Add((Socket)value);
        return m_InnserList.Count - 1;
    }

    public void Clear()
    {
        UnityEngine.Debug.Log("Cleared");
        m_InnserList.Clear();
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
        UnityEngine.Debug.Log("GetEnumerator");
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
        UnityEngine.Debug.Log("Remove");
        m_InnserList.Remove((Socket)value);
    }

    public void RemoveAt(int index)
    {
        UnityEngine.Debug.Log($"RemoveAt CurrentIEnumer {m_InnerIEnumerator.CurrentIndex}, RemoveIndex {index}");
        m_InnserList.RemoveAt(index);
    }

    public class SocketIEnumerator : IEnumerator
    {
        public List<Socket> m_ListRef;
        public int CurrentIndex = -1;

        public object Current => m_ListRef[CurrentIndex];

        public bool MoveNext()
        {
            UnityEngine.Debug.Log("MoveNext");
            return ++CurrentIndex < m_ListRef.Count;
        }

        public void Reset()
        {
            CurrentIndex = -1;
        }
    }
}

