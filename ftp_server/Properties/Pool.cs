using System.Collections.Generic;
using System.Net.Sockets;

namespace Server
{
    public class SocketAsyncEventArgsPool
    {
        private Stack<SocketAsyncEventArgs> Pool;

        public SocketAsyncEventArgsPool(int capacity)
        {
            Pool = new Stack<SocketAsyncEventArgs>(capacity);
        }

        public void Push(SocketAsyncEventArgs element)
        {
            lock (Pool)
            {
                Pool.Push(element);
            }
        }

        public SocketAsyncEventArgs Pop()
        {
            lock (Pool)
            {
                return Pool.Pop();
            }
        }

        public int StackSize()
        {
            int count;
            lock (Pool)
            {
                count = Pool.Count;
            }

            return count;
        }
    }

    class BufferManager
    {
        private readonly int _maximumClients;
        private readonly byte[] _mbuffer;                // the underlying byte array maintained by the Buffer Manager
        private readonly Stack<int> _freeIndexPool;
        private readonly int _mbufferSize; //Size of the buffer for per clients

        public BufferManager(int maximumClients, int bufferSize)
        {
            int numBytes = maximumClients * bufferSize;// Total buffer Size for users.
            _maximumClients = maximumClients;
            _mbufferSize = bufferSize;
            _freeIndexPool = new Stack<int>();
            _mbuffer = new byte[numBytes];
            InitStack();// Initialize the Buffer
        }

        // Allocates buffer space used by the buffer pool
        private void InitStack()
        {
            // divide the out for each SocketAsyncEventArg object           
            for (int i = 0; i < _maximumClients; i++)
            {
                _freeIndexPool.Push(i * _mbufferSize);//push index in the stack
            }
        }

        // Assigns a buffer from the buffer pool to the 
        // specified SocketAsyncEventArgs object
        //
        // <returns>true if the buffer was successfully set, else false</returns>
        public bool SetBuffer(SocketAsyncEventArgs args)
        {
            if (_freeIndexPool.Count > 0)
            {
                args.SetBuffer(_mbuffer, _freeIndexPool.Pop(), _mbufferSize);// Buffer to use, Offset to Start from, and Total buffer Size
            }
            else
            {
                return false;// Buffer is full!
            }
            return true;
        }

        // Removes the buffer from a SocketAsyncEventArg object.  
        // This frees the buffer back to the buffer pool
        public void FreeBuffer(SocketAsyncEventArgs args)// Frees buffer resource on args, and pushes offset to the index
        {
            _freeIndexPool.Push(args.Offset);
            args.SetBuffer(null, 0, 0);
        }

    }

    public class UserPool
    {
        private Stack<User> Pool;

        public UserPool(int capacity)
        {
            Pool = new Stack<User>(capacity);
        }

        public void Push(User element)
        {
            lock (Pool)
            {
                Pool.Push(element);
            }
        }

        public User Pop()
        {
            lock (Pool)
            {
                return Pool.Pop();
            }
        }

        public int StackSize()
        {
            int count;
            lock (Pool)
            {
                count = Pool.Count;
            }

            return count;
        }
    }

    public class CommunicatorPool
    {
        private Stack<Communicator> Pool;

        public CommunicatorPool(int capacity)
        {
            Pool = new Stack<Communicator>(capacity);
        }

        public void Push(Communicator element)
        {
            lock (Pool)
            {
                Pool.Push(element);
            }
        }

        public Communicator Pop()
        {
            lock (Pool)
            {
                return Pool.Pop();
            }
        }

        public int StackSize()
        {
            int count;
            lock (Pool)
            {
                count = Pool.Count;
            }

            return count;
        }
    }
}
