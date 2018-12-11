using System;
using System.Net.Sockets;
using System.Threading;

namespace Server {
public
class ReceiverMembers // parameters for per packet
{
  internal uint CurrentPosition;     // This specifies position in the packet
  internal uint PositionToReadUntil; // This will point to Header if header
                                     // hasn't been read, the whole packet after
                                     // it knows the packet size
  internal byte[] ReceiveBuffer =
      new byte[PacketConsts.ReceiverBufferSize + PacketConsts.HeaderSize];
  internal Packet DataPacket = new Packet();
}

public class SenderMembers // parameters for per packet
{
  internal uint CurrentPosition;
  internal uint PositionToSendUntil;
  internal byte[] SendBuffer =
      new byte[PacketConsts.SenderBufferSize + PacketConsts.HeaderSize];
}
/*
 * Communicator should terminate immediately if OperationState goes false
 */
public class Communicator {
  internal volatile bool OperationState = true;
  internal User UserToken {
    get;
  private
    set;
  }
private
  readonly SenderMembers _senderMembers;
private
  readonly ReceiverMembers _receiverMembers;
private
  readonly SocketAsyncEventArgs _sendArg;
private
  readonly SocketAsyncEventArgs _receiveArg;
private
  event EventHandler<SocketAsyncEventArgs> ReceiveHandler;
private
  event EventHandler<SocketAsyncEventArgs> SendHandler;

private
  Thread _sendPendingThread;
private
  Thread _receivePendingThread;
  internal uint ReceivedBytesTransferredCount;
  internal uint SentBytesTransferredCount;

  internal CancellationTokenSource CancelSendDequeue;

  internal Communicator(SocketAsyncEventArgs sendArg,
                        SocketAsyncEventArgs receiveArg) {
    ReceiveHandler =
        ReceiveCompleted;        // Add receive delagate to delegate Handler
    SendHandler = Send_completed; // Add send delegate to delagate Handler
    _sendArg = sendArg;
    _receiveArg = receiveArg;

    _sendArg.Completed += SendHandler;
    _receiveArg.Completed += ReceiveHandler;
    _senderMembers = new SenderMembers();
    _receiverMembers = new ReceiverMembers();

    CancelSendDequeue =
        new CancellationTokenSource(); // This will cancel send dequeueing
                                       // process if communicator terminates

    ResetReceiverMembers();
    ResetSenderMembers();
  }

  internal void SetUser(User user) { UserToken = user; }

  internal void StartSendThread() {
    Thread sendThread = new Thread(Send);
    SentBytesTransferredCount = 0;
    ResetSenderMembers();
            if ( UserToken?.ClientSocket != null)
            {
              sendThread.Start();
            }
  }

  internal void StartReceiveThread() {
    Thread receiveThread = new Thread(Receive);
    ReceivedBytesTransferredCount = 0;
    ResetReceiverMembers();
            if (UserToken?.ClientSocket != null)
            {
              receiveThread.Start();
            }
  }

private
  void Receive() // method for receiving the call
  {
    if (!OperationState || UserToken?.ClientSocket == null)
      return;
    try {
      bool pending = UserToken.ClientSocket.ReceiveAsync(
          _receiveArg); // This will spawn a new thread and call
      // Receive_completed as callback method

      if (pending)
        return;

      _receivePendingThread = new Thread(
          () => ReceiveCompleted(null, _receiveArg)); // Start New thread
      _receivePendingThread.Start();
    } catch (Exception exception) {
      ExceptionHandler(exception, CommunicatorError.ObjectDisposed);
    }
  }

private
  void Send() {
    if (!OperationState) {
      return; // Do nothing if Queue is empty
    }

    if (_senderMembers.CurrentPosition == 0) {
      Packet packetToSend = UserToken
          ?.DequeueSend(CancelSendDequeue); // This will wait until if There's
                                            // anything to Dequeue
      if (packetToSend == null)
        return;
      // Set the _senderMembers Buffer
      _senderMembers.SendBuffer =
          new byte[packetToSend.DataSize + PacketConsts.HeaderSize];
      Array.Copy(packetToSend.Header, _senderMembers.SendBuffer,
                 PacketConsts.HeaderSize); // copy header to SendBuffer
      Array.Copy(packetToSend.Data, 0, _senderMembers.SendBuffer,
                 PacketConsts.HeaderSize, packetToSend.DataSize);
      _senderMembers.PositionToSendUntil =
          packetToSend.DataSize + PacketConsts.HeaderSize;
    }

    uint copySize =
        _senderMembers.PositionToSendUntil - _senderMembers.CurrentPosition;
    if (PacketConsts.SendSize < copySize)
      copySize = PacketConsts.SendSize;
            if (!OperationState || UserToken?.ClientSocket == null)
              return;

            try {
              _sendArg.SetBuffer(
                  _sendArg.Offset,
                  (int)copySize); // set Buffer to process only copied bytes
              Array.Copy(_senderMembers.SendBuffer,
                         _senderMembers.CurrentPosition, _sendArg.Buffer,
                         _sendArg.Offset, copySize);
              _senderMembers.CurrentPosition += copySize;
              SentBytesTransferredCount += copySize;

              bool pending = UserToken.ClientSocket.SendAsync(
                  _sendArg); // This will spawn a new thread and call
                             // Send_completed as callback method
              if (pending)
                return;
              _sendPendingThread =
                  new Thread(() => ReceiveCompleted(null, _receiveArg));
              _sendPendingThread.Start();
            } catch (Exception e) {
              ExceptionHandler(e, CommunicatorError.ObjectDisposed);
            }
  }

  /** @brief callback methods after ReceiveAsync() and SendAsync()
   *  handles received data from ReceiveAsync()
   */
private
  void ReceiveCompleted(object sender, SocketAsyncEventArgs error) {
    if (error.LastOperation == SocketAsyncOperation.Receive && OperationState) {
      bool transferCompleted = false;
      try {
        if (error.BytesTransferred == 0) {
          throw new Exception("Socket Closed");
        }
        if (error.SocketError != SocketError.Success) {
          throw new Exception("Socket Error Occured");
        }
      } catch (Exception exception) {
        ExceptionHandler(exception, CommunicatorError.SocketError);
        return;
      } // Checks for received bytes

      if (OperationState)
        transferCompleted =
            ProcessReceive(); // returns if process was successful

      // Tell the sender, that Receive is completed
      if (transferCompleted)
        Receive(); // Receive if more bytes are left
    }
  }

private
  void Send_completed(object sender, SocketAsyncEventArgs e) {
    if (OperationState) {
      if (_senderMembers.CurrentPosition ==
              _senderMembers.PositionToSendUntil &&
          OperationState) // If Whole packet has been sent
        ResetSenderMembers();
      Send(); // This method will wait until Queue packet comes in to the queue
    }
  }

  /*
   * # of Cases that may happen
   * 1. Packet did not finish until byte transferred is all read
   *  -> currentposition pointer stores the position of receiver buffer, and
   * sourceBuffer pointer is set to start position of new bytes received. and
   * continues reading from there positionto Read parameter still points the
   * desired position in the buffer
   * 2. Packet finished before data from source buffer was all read
   *  -> After packet finishes, packet buffer is processed, and resetted.
   * current position buffer and positioin read buffer is again set to Header
   * size
   */

public
  bool ProcessReceive() { // Return value is whether process succeded
    byte[] destinationBuffer = _receiveArg.Buffer;
    uint bytesTransferred =
        (uint)_receiveArg
            .BytesTransferred; // Indicates how many bytes are received
    ReceivedBytesTransferredCount += bytesTransferred;
    int sourceBufferOffset = _receiveArg.Offset;
    uint sourceBufferPosition =
        (uint)sourceBufferOffset; // set Source position to Offset

    try {
      uint sourceRemainBytes = bytesTransferred;
      while (sourceRemainBytes > 0 && OperationState) {
        bool completed;
        if (_receiverMembers.CurrentPosition <
            PacketConsts.HeaderSize) // Header has not been completely read yet
        {
          _receiverMembers.PositionToReadUntil =
              PacketConsts.HeaderSize; // Set Read position to end of the Data
          completed = ReadBytes(destinationBuffer, ref sourceBufferPosition,
                                ref sourceRemainBytes);

          if (completed && OperationState) {
            if (Analyze_header()) { // Header was valid
              _receiverMembers.PositionToReadUntil =
                  PacketConsts.HeaderSize +
                  _receiverMembers.DataPacket
                      .DataSize;      // Set Read position to end of the Data
            } else {                  // Header was invalid
              ResetReceiverMembers(); // reset and read the header again
              throw new Exception("Invalid Header");
            }
          }
        } else {
          completed = ReadBytes(destinationBuffer, ref sourceBufferPosition,
                                ref sourceRemainBytes);
          if (completed && OperationState) // read all desired data, packet has
                                           // been completed
          {
            // Analyze Data
            Analyze_data();
            UserToken.EnqueueReceive(
                _receiverMembers.DataPacket); // Enqueue packet to the queue
                                              // ,waits if queue is full
            ResetReceiverMembers();
          }
        }
      }
      return true;
    } catch (Exception e) {
      // Send some data to tell the sender, Exception has occured in Receiving
      // part
      ExceptionHandler(e, CommunicatorError.HeaderError);
      return false;
    }
  }

private
  bool
  ReadBytes(byte[] buffer, ref uint sourcePosition,
            ref uint sourceRemainBytes) // reads bytes to the desired position
  { // returns true when it read to position to read
    if (!OperationState)
      return false;
    uint copysize = _receiverMembers.PositionToReadUntil -
                    _receiverMembers.CurrentPosition; // bytes to copy
    if (_receiverMembers.CurrentPosition >=
        _receiverMembers.PositionToReadUntil) {
      return true; // Has finished reading (No more to read)
    } else {
      if (sourceRemainBytes <
          copysize) // If received byte number is smaller than bytes to copy,
      {
        copysize = sourceRemainBytes; // Copy only transferred bytes
        Array.Copy(buffer, sourcePosition, _receiverMembers.ReceiveBuffer,
                   _receiverMembers.CurrentPosition, copysize);
        _receiverMembers.CurrentPosition += copysize;
        sourcePosition += copysize;
        sourceRemainBytes -= copysize;
        return false; // Hasn't reached to desired position due to transferred
                      // bytes wasn't big enough
      } else {
        Array.Copy(buffer, sourcePosition, _receiverMembers.ReceiveBuffer,
                   _receiverMembers.CurrentPosition, copysize);
        _receiverMembers.CurrentPosition += copysize;
        sourcePosition += copysize;
        sourceRemainBytes -= copysize;
        return true; // Reached to the desired position
      }
    }
  }

private
  bool Analyze_header() {
    bool validHeader = true;
    Array.Copy(_receiverMembers.ReceiveBuffer,
               _receiverMembers.DataPacket.Header, PacketConsts.HeaderSize);
    if (_receiverMembers.DataPacket.Header[HeaderMemberStartIndex.HeaderSign] ==
        PacketConsts.HeaderSign) {
      _receiverMembers.DataPacket.DataSize = BitConverter.ToUInt32(
          _receiverMembers.DataPacket.Header,
          HeaderMemberStartIndex.DataSize); // Get body data size

      _receiverMembers.DataPacket.RequestType =
          _receiverMembers.DataPacket
              .Header[HeaderMemberStartIndex.RequestType]; // Get request type
      _receiverMembers.DataPacket.PacketNumber = (ushort)(
          _receiverMembers.DataPacket
                  .Header[HeaderMemberStartIndex.PacketNum + 1]
              << 8 |
          _receiverMembers.DataPacket.Header[HeaderMemberStartIndex.PacketNum]);

      _receiverMembers.DataPacket.FileType =
          _receiverMembers.DataPacket.Header[HeaderMemberStartIndex.FileType];

      _receiverMembers.DataPacket.ProcessId =
          _receiverMembers.DataPacket.Header[HeaderMemberStartIndex.ProcessNum];

      if (_receiverMembers.DataPacket.Header[HeaderMemberStartIndex.EndPoint] ==
          PacketConsts.MoreBytes)
        _receiverMembers.DataPacket.MorePackets = true;
      else if (_receiverMembers.DataPacket
                   .Header[HeaderMemberStartIndex.EndPoint] ==
               PacketConsts.LastByte)
        _receiverMembers.DataPacket.MorePackets = false;
      else
        validHeader = false;
    } else {
      validHeader = false;
    }

    return validHeader;
  }

private
  void Analyze_data() {
    if (!OperationState)
      return;
    _receiverMembers.DataPacket.Data =
        new byte[_receiverMembers.DataPacket.DataSize];
    Array.Copy(_receiverMembers.ReceiveBuffer, PacketConsts.HeaderSize,
               _receiverMembers.DataPacket.Data, 0,
               _receiverMembers.DataPacket.DataSize);
  }

private
  void ResetReceiverMembers() {
    _receiverMembers.CurrentPosition = 0;
    _receiverMembers.PositionToReadUntil = 0;
    _receiverMembers.DataPacket = new Packet();
  }

private
  void ResetSenderMembers() {
    _senderMembers.CurrentPosition = 0;
    _senderMembers.PositionToSendUntil = 0;
  }

private
  void ExceptionHandler(Exception e, CommunicatorError error) {
            if (OperationState == false || UserToken?.ClientSocket == null)
              return;
            // Send some signal to Sender if exception has occured(according to
            // Exception type)
            Console.WriteLine(e.Message);
            OperationState = false;

            Packet suspendPacket = new Packet(){
                DataSize = 1,       FileType = (byte)FileType.NoFile,
                Data = new byte[1], MorePackets = false,
                PacketNumber = 0,   RequestType = (byte)PacketType.Suspend,
                ProcessId = 0 // For all packets
            };
            suspendPacket.Data[0] = (byte)error;
            OperationState = false;
            Console.WriteLine("Closing the Socket...");
            if (UserToken?.ClientSocket != null)
            {
              UserToken.EnqueueReceive(suspendPacket);
            }
  }
}
} // namespace Server
