using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Telepathy;

public class TelepathyTest : MonoBehaviour
{
    Telepathy.Server mServer;
    List<Telepathy.Client> mClients = new List<Client>();
    int connectionCount = 0;
    // Start is called before the first frame update
    void Start()
    {
        mServer = new Server();
        mServer.Start(3000);

        for(int i = 0; i < 1500; i++)
        {
            var newClinet = new Telepathy.Client();
            newClinet.Connect("localhost", 3000);
        }
    }

    // Update is called once per frame
    void Update()
    {
        Message msg;
        while(mServer.GetNextMessage(out msg))
        {
            if(msg.eventType == Telepathy.EventType.Connected)
            {
                connectionCount++;
            }
            else if(msg.eventType == Telepathy.EventType.Disconnected)
            {
                connectionCount--;
            }
        }
    }

    private void OnGUI()
    {
        GUILayout.Label(connectionCount.ToString());
    }
}
