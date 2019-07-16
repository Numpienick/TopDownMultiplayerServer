using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Networking;

public class Server : MonoBehaviour
{
    const int MAX_USER = 4;
    const int PORT = 62480;
    const int WEB_PORT = 62481;
    const int BYTE_SIZE = 1024;

    bool isStarted = false;
    byte reliableChannel;
    byte unreliableChannel;
    int hostId;
    byte error;

    List<int> playerCount = new List<int>();
    List<Net_HubInfo> hubs = new List<Net_HubInfo>();

    #region MonoBehaviour
    private void Start()
    {
        DontDestroyOnLoad(gameObject);
        Initialize();
    }

    private void Update()
    {
        UpdateMessagePump();
    }
    #endregion

    //Everything server related
    public void Initialize()
    {
        NetworkTransport.Init();

        ConnectionConfig cc = new ConnectionConfig();
        reliableChannel = cc.AddChannel(QosType.Reliable);
        unreliableChannel = cc.AddChannel(QosType.Unreliable);

        HostTopology topo = new HostTopology(cc, MAX_USER);

        //Server only code
        hostId = NetworkTransport.AddHost(topo, PORT, null);

        Debug.Log(string.Format("Opening connection on port {0} and webport {1}", PORT, WEB_PORT));
        isStarted = true;
    }

    public void Shutdown()
    {
        isStarted = false;
        NetworkTransport.Shutdown();
    }

    private void UpdateMessagePump()
    {
        if (!isStarted)
            return;

        int recHostId;      //Id for the platform where the message comes from
        int connectionId;   //Which user is sending the message?
        int channelId;      //Which lane is he sending that message from

        byte[] recBuffer = new byte[BYTE_SIZE];     //Holds the message that comes through
        int dataSize;                               //Size of the message

        NetworkEventType type = NetworkTransport.Receive(out recHostId, out connectionId, out channelId, recBuffer, BYTE_SIZE, out dataSize, out error);
        switch (type)
        {
            case NetworkEventType.Nothing:
                break;

            case NetworkEventType.ConnectEvent:
                Debug.Log(string.Format("User {0} has connected through host {1}", connectionId, recHostId));
                playerCount.Add(connectionId);
                break;

            case NetworkEventType.DisconnectEvent:
                Debug.Log(string.Format("User {0} has disconnected ;-;", connectionId));
                if (playerCount.Count > 0)
                    playerCount.Remove(connectionId);

                Net_PlayerInfo player = GetCurrentPlayer(connectionId);
                if (player != null)
                {
                    Net_HubInfo hub = GetPlayerHub(player);
                    if (hub != null)
                    {
                        Net_PlayerDisconnected discPlayer = new Net_PlayerDisconnected(player);
                        hub.players.Remove(player);
                        SendActivePlayers(discPlayer, hub.players, reliableChannel);
                        if (player.playerType == "host")
                        {
                            Debug.Log("remove hubs!~!!");
                            SendClients(hub);
                            hubs.Remove(hub);
                        }
                    }
                }
                break;

            case NetworkEventType.DataEvent:
                BinaryFormatter formatter = new BinaryFormatter();
                MemoryStream ms = new MemoryStream(recBuffer);
                NetMsg msg = (NetMsg)formatter.Deserialize(ms);

                OnData(connectionId, channelId, recHostId, msg);
                break;

            default:
            case NetworkEventType.BroadcastEvent:
                Debug.Log("Unexpected network event type");
                break;
        }
    }

    #region OnData
    void OnData(int cnnId, int channelId, int recHostId, NetMsg msg)
    {
        switch (msg.OperationCode)
        {
            case NetOperationCode.none:
                Debug.Log("Unexpected NETOP");
                break;

            case NetOperationCode.createAccount:
                CreateAccount(cnnId, channelId, recHostId, (Net_CreateAccount)msg);
                break;

            case NetOperationCode.playerInfo:
                AddPlayerToServer((Net_PlayerInfo)msg);
                break;

            case NetOperationCode.readyUp:
                ReadyUp((Net_ReadyUp)msg);
                break;

            case NetOperationCode.movement:
                SendMovement((Net_Movement)msg);
                break;
        }
    }

    void SendMovement(Net_Movement msg)
    {
        Net_HubInfo hub = GetPlayerHub(msg.player);
        List<Net_PlayerInfo> temp = new List<Net_PlayerInfo>(hub.players);        
        Net_PlayerInfo sender = temp.Find(x => x.connectionId == msg.player.connectionId);
        temp.Remove(sender);
        SendActivePlayers(msg, temp, unreliableChannel);
    }

    void CreateAccount(int cnnId, int channelId, int recHostId, Net_CreateAccount ca)
    {
        Net_OnCreateAccount oca = new Net_OnCreateAccount(cnnId, ca.userName, ca.playerType);

        SendClient(cnnId, oca);
    }

    void AddPlayerToServer(Net_PlayerInfo pi)
    {
        if (pi.playerType == "host")
            CreateHub(pi);
        Net_HubInfo hub = GetPlayerHub(pi);
        if (hub != null)
        {
            hub.players.Add(pi);
            for (int i = 0; i < hub.players.Count; i++)
            {
                Net_PlayerInfo currentPlayer = hub.players[i];
                if (currentPlayer != pi)
                {
                    SendClient(pi.connectionId, currentPlayer);
                    Debug.Log("sending players to joined client");
                }
            }
            SendActivePlayers(pi, hub.players, reliableChannel);
        }
        else
            for (int i = 0; i < hubs.Count; i++)
            {
                SendClient(pi.connectionId, hubs[i]);
                Debug.Log("sending " + pi.userName + " the hub");
            }
    }

    void CreateHub(Net_PlayerInfo host)
    {
        Debug.Log("Creating Hub");
        Net_HubInfo hub = (new Net_HubInfo(string.Format("{0}'s game", host.userName), host)
        {
            players = new List<Net_PlayerInfo>(),
        });
        hubs.Add(hub);
    }

    void ReadyUp(Net_ReadyUp msg)
    {
        Net_PlayerInfo player = GetCurrentPlayer(msg.connectionId);
        Net_HubInfo hub = GetPlayerHub(player);
        player.ready = msg.ready;
        Debug.Log(player.userName + " " + player.ready);
        Debug.Log("Players are: " + hub.players.TrueForAll(IsEveryPlayerReady));

        if (hub.players.TrueForAll(IsEveryPlayerReady))
        {
            SendActivePlayers(new Net_ReadyUp(0, true), hub.players, reliableChannel);
        }
    }
    #endregion

    bool IsEveryPlayerReady(Net_PlayerInfo player)
    {
        Debug.Log(player.userName + " is " + player.ready);
        if (player.ready == true)
            return true;
        else
            return false;
    }

    Net_PlayerInfo GetCurrentPlayer(int playerId)
    {
        for (int i = 0; i < hubs.Count; i++)
        {
            Net_PlayerInfo player = hubs[i].players.Find(x => x.connectionId == playerId);

            if (player != null)
                return player;
        }
        return null;
    }

    Net_HubInfo GetPlayerHub(Net_PlayerInfo player)
    {
        Net_HubInfo hub = hubs.Find(x => x.hubName == player.joinedHub);
        if (hub != null)
            return hub;
        else
            return null;
    }

    #region Send
    public void SendClient(int cnnId, NetMsg msg)
    {
        //This holds the data
        byte[] buffer = new byte[BYTE_SIZE];

        //Crushes data into a byte[]
        BinaryFormatter formatter = new BinaryFormatter();
        MemoryStream ms = new MemoryStream(buffer);
        formatter.Serialize(ms, msg);

        NetworkTransport.Send(hostId, cnnId, reliableChannel, buffer, BYTE_SIZE, out error);
    }

    public void SendClients(NetMsg msg)
    {
        //This holds the data
        byte[] buffer = new byte[BYTE_SIZE];

        //Crushes data into a byte[]
        BinaryFormatter formatter = new BinaryFormatter();
        MemoryStream ms = new MemoryStream(buffer);
        formatter.Serialize(ms, msg);

        for (int i = 1; i <= playerCount.Count; i++)
        {
            NetworkTransport.Send(hostId, i, reliableChannel, buffer, BYTE_SIZE, out error);
        }
    }

    public void SendActivePlayers(NetMsg msg, List<Net_PlayerInfo> list, byte channel)
    {
        //This holds the data
        byte[] buffer = new byte[BYTE_SIZE];

        //Crushes data into a byte[]
        BinaryFormatter formatter = new BinaryFormatter();
        MemoryStream ms = new MemoryStream(buffer);
        formatter.Serialize(ms, msg);

        for (int i = 0; i < list.Count; i++)
        {
            NetworkTransport.Send(hostId, list[i].connectionId, channel, buffer, BYTE_SIZE, out error);
        }
    }
    #endregion   
}