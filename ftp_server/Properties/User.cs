using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace Server
{
    public abstract class Client
    {
        internal BlockingCollection<Packet> ReceiveQueue = new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());
        internal BlockingCollection<Packet> SendQueue = new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());
        protected Socket Socket;

        public Socket ClientSocket
        {
            get => Socket;
            protected set => Socket = value;
        }

        internal int CountSend()
        {
            int returnNum;
            lock (SendQueue)
            {
                returnNum = SendQueue.Count;
            }

            return returnNum;
        }

        internal int CountReceive()
        {
            return ReceiveQueue.Count;
        }

        internal Packet DequeueSend()
        {
            return SendQueue.Take();
        }

        internal Packet DequeueSend(CancellationTokenSource cancel)
        {
            try
            {
                return SendQueue.Take(cancel.Token);
            }
            catch
            {
                return null;
            }
        }

        internal Packet DequeueReceive()
        {
            return ReceiveQueue.Take();
        }

        internal Packet DequeueReceive(CancellationTokenSource cancel)
        {
            try
            {
                return ReceiveQueue.Take(cancel.Token);
            }
            catch
            {
                return null;
            }
        }

        internal Packet PeekOfSend()
        {
            return SendQueue.First();
        }

        internal void EnqueueSend(Packet packet)
        {
            SendQueue.Add(packet);
        }

        internal void EnqueueReceive(Packet packet)
        {
            ReceiveQueue.Add(packet);
        }

    }
    public class User : Client //Stores user information
    {
        internal uint UserId;
        internal User(uint id)
        {
            UserId = id;
        }


        internal void SetUserSocket(Socket socket)
        {
            ClientSocket = socket;
        }

    }

    class Manager : Client //Stores information for Server managers
    {
        private int _managerID;
        public Manager(int managerID)
        {
            _managerID = managerID;
        }

    }
}
