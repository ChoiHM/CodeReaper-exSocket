using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace CodeReaper
{
    //****************************************************************
    //                  Extended Socket Class 4.0.6
    //****************************************************************
    // Extended Socket Class for .NET Framework 4.0
    // Windows XP/7/8/10
    // - H.M Choi
    // ---------------------------------------------------------------
    public class exSocket
    {
        #region Event Handler
        //에러 발생 event
        public event onErrorEventHandler onError;
        public delegate void onErrorEventHandler(int errCode, string Description);
        //송신 event
        //public event onSendCompleteEventHandler onSendComplete;
        public delegate void onSendCompleteEventHandler(string Data);
        //수신 event
        public event onDataArrivalEventHandler onDataArrival;
        public delegate void onDataArrivalEventHandler(object sender, byte[] Data, int bytesRead);
        //연결해제 event
        public event onDisconnectEventHandler onDisconnect;
        public delegate void onDisconnectEventHandler(object sender);
        //연결 event
        public event onConnectEventHandler onConnect;
        public delegate void onConnectEventHandler(object sender);
        /// 타이머 Tick 이벤트
        public event EventHandler Tick;
        #endregion

        public bool isServer { get; set; }
        protected TcpListener sckListener = null;
        protected Socket innerSocket = null;

        public const int BUFFER_SIZE = 4096;
        protected byte[] BUFFER = new byte[BUFFER_SIZE];

        public string remoteIP { get; set; }
        public int remotePort { get; set; }

        protected eState _State = eState.Closed;
        public eState State { get { return _State; } set { _State = value; } }
        public enum eState { Closed = 0, Connecting = 1, Connected = 2, Listening = 5 }
        public enum eSendMode { String, Mixed }

        protected System.Timers.Timer tmr = new System.Timers.Timer(1000) { Enabled = false };
        public int ConnectInterval { get { return int.Parse(tmr.Interval.ToString()); } set { tmr.Interval = value; } }
        public bool AutoConnectEnable { get { return tmr.Enabled; } set { tmr.Enabled = value; } }

        public exSocket()
        {
            tmr.Elapsed += tmr_Elapsed;
        }

        public exSocket(string ip, int port)
        {
            tmr.Elapsed += tmr_Elapsed;
            remoteIP = ip;
            remotePort = port;
        }

        public bool isConnected
        {
            get
            {
                if (innerSocket != null) { return innerSocket.Connected; }
                return false;
            }
        }

        protected class asyncObj
        {
            public byte[] Buffer = new byte[BUFFER_SIZE];
            public Socket _socket;
        }
        protected asyncObj obj = new asyncObj();


        protected void tmr_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Tick != null) { Tick(this, e); }
            if (AutoConnectEnable) { Connect(); }
        }

        #region Client Side
        public void Connect()
        {
            try
            {
                if (State == eState.Connecting)
                {
                    if (onError != null) { onError(11, "Socket Connect error (Now work on connecting)"); }
                    return;
                }
                if (State == eState.Connected)
                {
                    if (onError != null) { onError(12, "Socket Connect error (Already connected)"); }
                    return;
                }
                if (State == eState.Listening)
                {
                    if (onError != null) { onError(13, "Socket Connect error (Listening중에 Connect를 시도할 수 없음)"); }
                    return;
                }

                //Disconnect(); // make sure disconnecting

                State = eState.Connecting;
                innerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        obj._socket = innerSocket;
                        innerSocket.BeginConnect(remoteIP, remotePort, new AsyncCallback(procEndConnect), obj);
                        Debug.WriteLine(string.Format("Socket connecting to {0}:{1}...", remoteIP, remotePort));
                    }
                    catch
                    {
                        Disconnect();
                        return;
                    }
                }, TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                State = eState.Closed;
                Debug.WriteLine("Connect error");
                if (onError != null) { onError(19, "Connect error (" + ex.Message + ")"); }
            }
        }
        protected void procEndConnect(IAsyncResult ar)
        {
            if (ar == null || ar.AsyncState == null)
            {
                State = eState.Closed;
                return;
            }

            asyncObj ao = (asyncObj)ar.AsyncState;
            try
            {
                innerSocket = ao._socket;
                if (innerSocket.Connected == false)
                {
                    Disconnect();
                    return;
                }
                ao._socket.EndConnect(ar);

                if (innerSocket == null)
                {
                    if (onError != null) { onError(23, "procEndConnect error : InnerSocket=null)"); }
                    Disconnect();
                }

                if (this.onConnect != null) { this.onConnect(this); }
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(21, string.Format("procEndConnect error (Ex.Message={0})", ex.Message)); }
                State = eState.Closed;
                return;
            }

            if (isConnected)
            {
                State = eState.Connected;
                Debug.WriteLine(string.Format("Socket connected to {0}", innerSocket.RemoteEndPoint.ToString()));
                procBeginReceive();
            }
            else
            {
                State = eState.Closed;
            }
        }


        protected void procBeginReceive()
        {
            State = eState.Connected;
            try
            {
                obj._socket = innerSocket;
                innerSocket.BeginReceive(BUFFER, 0, BUFFER_SIZE, SocketFlags.None, new AsyncCallback(procEndReceive), obj);
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(31, "procBeginReceive error (" + ex.Message + ")"); }
                Disconnect();
            }
        }

        protected void procEndReceive(IAsyncResult ar)
        {
            SocketError err = SocketError.SocketError;

            try
            {
                asyncObj ao = (asyncObj)ar.AsyncState;
                int bytesToRead = 0;
                err = SocketError.SocketError;
                bytesToRead = ao._socket.EndReceive(ar, out err);

                if (bytesToRead > 0)
                {

                    byte[] rcvBuff = new byte[bytesToRead];
                    Array.Copy(BUFFER, rcvBuff, bytesToRead);

                    if (this.onDataArrival != null) { this.onDataArrival(this, rcvBuff, bytesToRead); }
                    procBeginReceive();
                }
                else
                {
                    Disconnect();
                }
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(41, string.Format("procEndReceive error (Code={0}) (Ex.Message={1})", err.ToString(), ex.Message)); }
                Disconnect();
            }
        }

        #endregion

        #region Server Side
        public int LocalPort { get; set; }
        public void Listen()
        {
            if (State == eState.Connecting)
            {
                if (onError != null) { onError(51, "Socket Listen error (Now working on listen)"); }
                return;
            }
            if (State == eState.Connected)
            {
                if (onError != null) { onError(52, "Socket Listen error (Already connected)"); }
                return;
            }
            if (State == eState.Listening)
            {
                if (onError != null) { onError(53, "Socket Listen error (Listening중에 재시도할 수 없음)"); }
                return;
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    isServer = true;
                    sckListener = new TcpListener(IPAddress.Any, LocalPort);
                    sckListener.ExclusiveAddressUse = true;
                    sckListener.Start(10);
                    State = eState.Listening;
                    obj._socket = sckListener.Server;
                    sckListener.BeginAcceptTcpClient(new AsyncCallback(procEndAccept), obj);
                }
                catch (Exception ex)
                {
                    if (onError != null) { onError(53, string.Format("Socket Listen error (Ex.Message={0})", ex.Message)); }
                    return;
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void procEndAccept(IAsyncResult ar)
        {
            asyncObj ao = (asyncObj)ar.AsyncState;
            try
            {
                innerSocket = ao._socket.EndAccept(ar);
                State = eState.Connected;
                if (this.onConnect != null) { this.onConnect(this); }
                procBeginReceive();
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(61, string.Format("procEndAccept error (Ex.Message={0})", ex.Message)); }
                Disconnect();
            }
        }

        #endregion

        #region Disconnect
        public void Disconnect()
        {
            try
            {
                if (innerSocket != null)
                {
                    if (innerSocket.Connected)
                    {
                        innerSocket.Shutdown(SocketShutdown.Both);
                        //innerSocket.Disconnect(true);
                    }

                    innerSocket.Close();
                    innerSocket = null;
                }
                if (sckListener != null)
                {
                    sckListener.Stop();
                    if (sckListener.Server.Connected)
                    {
                        sckListener.Server.Shutdown(SocketShutdown.Both);
                    }
                    sckListener.Server.Close();
                    sckListener = null;
                }
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(101, "Socket Disconnect Error (" + ex.Message + ")"); }
            }
            finally
            {
                State = eState.Closed;
                innerSocket = null;
                if (this.onDisconnect != null) { this.onDisconnect(this); }
            }
        }


        private void procEndDisconnect(IAsyncResult ar)
        {
            asyncObj ao = (asyncObj)ar.AsyncState;
            try
            {
                ao._socket.EndDisconnect(ar);
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(109, string.Format("procEndDisconnect error (Ex.Message={0})", ex.Message)); }
                Disconnect();
            }
        }
        #endregion

        #region Send Part
        public void SendData(byte[] Data, eSendMode mode = eSendMode.String)
        {
            try
            {
                if (State != eState.Connected)
                {
                    if (onError != null) { onError(111, "Send data error (Not Connected)"); }
                    return;
                }
                byte[] byteData = Data;
                StringBuilder strTemp = new StringBuilder();
                for (int i = 0; i < byteData.Length; i++)
                {
                    if (byteData[i] < 31)
                    {
                        strTemp.Append("[" + byteData[i] + "]");
                    }
                    else
                    {
                        strTemp.Append(Convert.ToChar(byteData[i]));
                    }
                }

                if (mode == eSendMode.String)
                {
                    innerSocket.Send(byteData);
                }
                else
                {
                    innerSocket.Send(byteData);
                }
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(119, "Send data error (" + ex.Message + ")"); }
                Disconnect();
            }
        }

        public void SendData(string Data, eSendMode mode = eSendMode.String)
        {
            try
            {
                if (State != eState.Connected)
                {
                    if (onError != null) { onError(121, "Send data error (Not Connected)"); }
                    return;
                }

                byte[] byteData = Encoding.UTF8.GetBytes(Data);
                StringBuilder strTemp = new StringBuilder();
                for (int i = 0; i < byteData.Length; i++)
                {
                    if (byteData[i] < 31)
                    {
                        strTemp.Append("[" + byteData[i] + "]");
                    }
                    else
                    {
                        strTemp.Append(Convert.ToChar(byteData[i]));
                    }
                }
                if (mode == eSendMode.String)
                {
                    innerSocket.Send(byteData);
                }
                else
                {
                    innerSocket.Send(byteData);
                }
            }
            catch (Exception ex)
            {
                if (onError != null) { onError(129, "Socket SendData Error (" + ex.Message + ")"); }
                Disconnect();
            }
        }
        #endregion

    }
}
