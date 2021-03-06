/*
 *  Connection.cs
 *
 *  Copyright 2016 MZ Automation GmbH
 *
 *  This file is part of lib60870.NET
 *
 *  lib60870.NET is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  lib60870.NET is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License
 *  along with lib60870.NET.  If not, see <http://www.gnu.org/licenses/>.
 *
 *  See COPYING file for the complete license text.
 */

using System;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace lib60870
{
    /// <summary>
    /// 连接异常-定义一个
    /// </summary>
    public class ConnectionException : Exception
    {
        public ConnectionException(string message)
            : base(message)
        {
        }

        public ConnectionException(string message, Exception e)
            : base(message, e)
        {
        }
    }

    /// <summary>
    /// 连接事件
    /// </summary>
    public enum ConnectionEvent
    {
        /// <summary>
        /// 已连接
        /// </summary>
        OPENED = 0,
        /// <summary>
        /// 关闭
        /// </summary>
        CLOSED = 1,
        /// <summary>
        /// STARTDT确认已收到
        /// </summary>
        STARTDT_CON_RECEIVED = 2,
        /// <summary>
        /// STOPDT确认已收到
        /// </summary>
        STOPDT_CON_RECEIVED = 3
    }

    /// <summary>
    /// ASDU received handler.
    /// </summary>
    public delegate bool ASDUReceivedHandler(object parameter, ASDU asdu);

    public delegate void ConnectionHandler(object parameter, ConnectionEvent connectionEvent);

    public class Connection
    {
        /// <summary>
        /// <para>U帧，开启命令</para>
        /// <para>  由控制中心发出的开启命令</para>
        /// <para>  用于TCP链路刚建立或者在被控站接受停止命令后的重新激活被控站数据传输</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] STARTDT_ACT_MSG = new byte[] { 0x68, 0x04, 0x07, 0x00, 0x00, 0x00 };

        /// <summary>
        /// <para>U帧，开启确认</para>
        /// <para>  由被控站回答“开启确认”</para>
        /// <para>  只用于回答控制站的开启命令</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] STARTDT_CON_MSG = new byte[] { 0x68, 0x04, 0x0b, 0x00, 0x00, 0x00 };

        /// <summary>
        /// <para>U帧，停止命令</para>
        /// <para>  由控制中心发出的停止命令</para>
        /// <para>  用于控制站对被控站停止激活数据传输</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] STOPDT_ACT_MSG = new byte[] { 0x68, 0x04, 0x13, 0x00, 0x00, 0x00 };

        /// <summary>
        /// <para>U帧，停止确认</para>
        /// <para>  由被控站回答的停止确认</para>
        /// <para>  只用于回答控制站的停止命令</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] STOPDT_CON_MSG = new byte[] { 0x68, 0x04, 0x23, 0x00, 0x00, 0x00 };
        /// <summary>
        /// <para>U帧，测试命令</para>
        /// <para>  控制站和被控站都可以发送测试命令</para>
        /// <para>  一旦其中一方已经发出了测试命令，另一方必须回答，且不需要在发送测试命令</para>
        /// <para>  在连接建立后就进入检查通道空闲的超时事件，一旦空闲时间大于t3，就发出测试命令</para>
        /// <para>  接收方收到任何一条I格式，S格式，U格式都使t3清零重新开始计时</para>
        /// <para>  用于测试链路是否完好</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] TESTFR_ACT_MSG = new byte[] { 0x68, 0x04, 0x43, 0x00, 0x00, 0x00 };
        /// <summary>
        /// <para>U帧，停止确认</para>
        /// <para>  控制站和被控站都可以发送测试确认命令</para>
        /// <para>  一旦其中一方已经发出了测试命令，另一方必须回答，且不需要在发送测试命令</para>
        /// <para>  用于测试链路是否完好</para>
        /// <para>  格式和内容固定</para>
        /// </summary>
        static byte[] TESTFR_CON_MSG = new byte[] { 0x68, 0x04, 0x83, 0x00, 0x00, 0x00 };

        /// <summary>
        /// 发送计数器
        /// </summary>
        private int sendCount;
        /// <summary>
        /// 接收计数器
        /// </summary>
        private int receiveCount;

        /// <summary>
        /// 未确认的消息数量
        /// </summary>
        private int unconfirmedMessages; /* number of unconfirmed messages received */
        /// <summary>
        /// 上一次确认时间
        /// </summary>
        private long lastConfirmationTime; /* timestamp when the last confirmation message was sent */

        private Socket socket;

        private bool autostart = true;

        public bool Autostart
        {
            get
            {
                return this.autostart;
            }
            set
            {
                this.autostart = value;
            }
        }

        /// <summary>
        /// 对方主机名
        /// </summary>
        private string hostname;
        /// <summary>
        /// 端口号--默认2404
        /// </summary>
        private int tcpPort;

        /// <summary>
        ///  是否正在通信
        /// </summary>
        private bool running = false;
        private bool connecting = false;
        private bool socketError;
        private SocketException lastException;

        private bool debugOutput = false;

        private void ResetConnection()
        {
            sendCount = 0;
            receiveCount = 0;
            unconfirmedMessages = 0;
            lastConfirmationTime = System.Int64.MaxValue;
            socketError = false;
            lastException = null;
        }

        public bool DebugOutput
        {
            get
            {
                return this.debugOutput;
            }
            set
            {
                debugOutput = value;
            }
        }

        /// <summary>
        /// 连接超时毫秒数（t0*1000）
        /// </summary>
        private int connectTimeoutInMs = 1000;

        private ConnectionParameters parameters;

        ASDUReceivedHandler asduReceivedHandler = null;
        object asduReceivedHandlerParameter = null;

        ConnectionHandler connectionHandler = null;
        object connectionHandlerParameter = null;

        /// <summary>
        /// 发送一个S帧，把当前已收到的信息计数发出去。。。
        /// </summary>
        private void sendSMessage()
        {
            byte[] msg = new byte[6];

            msg[0] = 0x68;
            msg[1] = 0x04;
            msg[2] = 0x01;
            msg[3] = 0;
            msg[4] = (byte)((receiveCount % 128) * 2);
            msg[5] = (byte)(receiveCount / 128);

            socket.Send(msg);
        }


        /// <summary>
        /// 发送I帧
        /// </summary>
        /// <param name="frame"></param>
        private void sendIMessage(Frame frame)
        {
            //更新发送、接收计数器
            frame.PrepareToSend(sendCount, receiveCount);

            if (running)
            {
                socket.Send(frame.GetBuffer(), frame.GetMsgSize(), SocketFlags.None);
                sendCount++;
            }
            else
            {
                if (lastException != null)
                    throw new ConnectionException(lastException.Message, lastException);
                else
                    throw new ConnectionException("not connected", new SocketException(10057));
            }

        }

        /// <summary>
        /// 设置连接，主机名/ip，连接参数，端口
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="parameters"></param>
        /// <param name="tcpPort"></param>
        private void setup(string hostname, ConnectionParameters parameters, int tcpPort)
        {
            this.hostname = hostname;
            this.parameters = parameters;
            this.tcpPort = tcpPort;
            this.connectTimeoutInMs = parameters.T0 * 1000;
        }

        /// <summary>
        /// 构造函数，使用主机名初始化，端口默认为2404
        /// </summary>
        /// <param name="hostname"></param>
        public Connection(string hostname)
        {
            setup(hostname, new ConnectionParameters(), 2404);
        }

        /// <summary>
        /// 使用主机和端口初始化
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="tcpPort"></param>
        public Connection(string hostname, int tcpPort)
        {
            setup(hostname, new ConnectionParameters(), tcpPort);
        }

        /// <summary>
        /// 使用主机和连接参数初始化
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="parameters"></param>
        public Connection(string hostname, ConnectionParameters parameters)
        {
            setup(hostname, parameters.clone(), 2404);
        }

        /// <summary>
        /// 使用主机名、端口号、和连接参数初始化
        /// </summary>
        /// <param name="hostname"></param>
        /// <param name="tcpPort"></param>
        /// <param name="parameters"></param>
        public Connection(string hostname, int tcpPort, ConnectionParameters parameters)
        {
            setup(hostname, parameters.clone(), tcpPort);
        }

        /// <summary>
        /// 设置超时时间
        /// </summary>
        /// <param name="millies"></param>
        public void SetConnectTimeout(int millies)
        {
            this.connectTimeoutInMs = millies;
        }

        /// <summary>
        /// 编码“数据单元标识符”将类型标识、可变结构限定词，传送原因和公共地址编码进去
        /// <para>注：这里是ASDU的头，应该首先调用</para>
        /// </summary>
        /// <param name="frame">需要编码的帧</param>
        /// <param name="typeId">类型标识</param>
        /// <param name="vsq">
        /// <para> 可变结构限定词  SQ+number</para>
        /// <para> SQ 离散或者顺序，0：离散，1连续</para>
        /// <para> number，信息对象数目 0~127</para>
        /// </param>
        /// <param name="cot">传送原因</param>
        /// <param name="ca">公共地址</param>
        private void EncodeIdentificationField(Frame frame, TypeID typeId,
                                               int vsq, CauseOfTransmission cot, int ca)
        {
            frame.SetNextByte((byte)typeId);
            frame.SetNextByte((byte)vsq); /* SQ:false; NumIX:1 */

            /* encode COT */
            frame.SetNextByte((byte)cot);
            if (parameters.SizeOfCOT == 2)
                frame.SetNextByte((byte)parameters.OriginatorAddress);

            /* encode CA */
            frame.SetNextByte((byte)(ca & 0xff));
            if (parameters.SizeOfCA == 2)
                frame.SetNextByte((byte)((ca & 0xff00) >> 8));
        }

        /// <summary>
        /// 将信息对象地址编码进去
        /// <para>这里是所有信息对象的头，应该在EncodeIdentificationField之后立即调用此函数</para>
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="ioa">信息对象地址</param>
        private void EncodeIOA(Frame frame, int ioa)
        {
            frame.SetNextByte((byte)(ioa & 0xff));

            if (parameters.SizeOfIOA > 1)
                frame.SetNextByte((byte)((ioa / 0x100) & 0xff));

            if (parameters.SizeOfIOA > 1)
                frame.SetNextByte((byte)((ioa / 0x10000) & 0xff));
        }

        /// <summary>
        /// 发送站召唤（类型标识100），Sends the interrogation command.
        /// <para>站召唤和组召唤都行</para>
        /// <para>站端回答镜像报文确认或否定</para>
        /// </summary>
        /// <param name="cot">
        /// 传送原因Cause of transmission
        /// <para>控制方向</para>
        /// <para>  6  激活</para>
        /// <para>  8  停止激活</para>
        /// <para>监视方向</para>
        /// <para>  7  激活确认</para>
        /// <para>  9  停止激活确认</para>
        /// <para>  10  激活终止</para>
        /// <para>  44  未知的类型标识</para>
        /// <para>  45  未知的传送原因</para>
        /// <para>  46  未知的应用服务数据单元公共地址 cot</para>
        /// <para>  47  未知的信息对象地址</para>
        /// </param>
        /// <param name="ca">公共地址Common address</param>
        /// <param name="qoi">
        /// 召唤限定词（20全站召唤），Qualifier of interrogation (20 = station interrogation)
        /// <para>0  未用</para>
        /// <para>1-19  未定义</para>
        /// <para>20    全站召唤</para>
        /// <para>21-28    分别为召唤第1-8组信息（遥信信息）</para>
        /// <para>29-34    分别为召唤第9-14组信息（遥测信息）</para>
        /// <para>35    召唤第15组信息（档位信息）</para>
        /// <para>36    召唤第16组信息（远动中断状态信息）</para>
        /// <para>37-255    未定义</para>
        /// </param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendInterrogationCommand(CauseOfTransmission cot, int ca, byte qoi)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_IC_NA_1, 1, cot, ca);

            EncodeIOA(frame, 0);

            /* encode COI (7.2.6.21) */
            frame.SetNextByte(qoi); /* 20 = station interrogation */

            if (debugOutput)
                Console.WriteLine("Encoded C_IC_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// 发送累计量召唤命令，Sends the counter interrogation command (C_CI_NA_1 typeID: 101)
        /// </summary>
        /// <param name="cot">传送原因 Cause of transmission</param>
        /// <param name="ca">公共地址Common address</param>
        /// <param name="qcc">
        /// 召唤限定词（应该跟站召一样，下面列出来参考下）Qualifier of counter interrogation command
        /// <para>0  未用</para>
        /// <para>1-19  未定义</para>
        /// <para>20    全站召唤</para>
        /// <para>21-28    分别为召唤第1-8组信息（遥信信息）</para>
        /// <para>29-34    分别为召唤第9-14组信息（遥测信息）</para>
        /// <para>35    召唤第15组信息（档位信息）</para>
        /// <para>36    召唤第16组信息（远动中断状态信息）</para>
        /// <para>37-255    未定义</para>
        /// </param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendCounterInterrogationCommand(CauseOfTransmission cot, int ca, byte qcc)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_CI_NA_1, 1, cot, ca);

            EncodeIOA(frame, 0);

            /* encode QCC */
            frame.SetNextByte(qcc);

            if (debugOutput)
                Console.WriteLine("Encoded C_CI_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// 读命令（102）Sends a read command (C_RD_NA_1 typeID: 102).
        /// <para>召唤单个信息，单个遥测、遥信的当前值读取</para>
        /// </summary>
        /// 
        /// This will send a read command C_RC_NA_1 (102) to the slave/outstation. The COT is always REQUEST (5).
        /// It is used to implement the cyclical polling of data application function.
        /// 
        /// 传送原因：
        ///     控制方向
        ///         5： 请求
        ///     监视方向
        ///         5： 被请求
        ///         44  未知的类型标识
        ///         45  未知的传送原因
        ///         46  未知的应用服务数据单元公共地址 cot
        ///         47  未知的信息对象地址
        /// <param name="ca">公共地址Common address</param>
        /// <param name="ioa">信息对象地址Information object address</param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendReadCommand(int ca, int ioa)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_RD_NA_1, 1, CauseOfTransmission.REQUEST, ca);

            EncodeIOA(frame, ioa);

            if (debugOutput)
                Console.WriteLine("Encoded C_RD_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// 发送时钟同步命令（103）Sends a clock synchronization command (C_CS_NA_1 typeID: 103).
        /// <para>只用于站端没有GPS的情况</para>
        /// <para>使用104进行同步时，无须测量通道延时</para>
        /// </summary>
        /// 传送原因：
        ///     控制方向
        ///         6： 激活
        ///     监视方向
        ///         7： 激活确认
        ///         10  激活终止
        ///         44  未知的类型标识
        ///         45  未知的传送原因
        ///         46  未知的应用服务数据单元公共地址 cot
        ///         47  未知的信息对象地址
        /// <param name="ca">公共地址Common address</param>
        /// <param name="time">时间日期the new time to set</param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendClockSyncCommand(int ca, CP56Time2a time)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_CS_NA_1, 1, CauseOfTransmission.ACTIVATION, ca);

            EncodeIOA(frame, 0);

            frame.AppendBytes(time.GetEncodedValue());

            if (debugOutput)
                Console.WriteLine("Encoded C_CS_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// Sends a test command (C_TS_NA_1 typeID: 104).
        /// </summary>
        /// 
        /// Not required and supported by IEC 60870-5-104. 
        /// 
        /// <param name="ca">Common address</param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendTestCommand(int ca)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_TS_NA_1, 1, CauseOfTransmission.ACTIVATION, ca);

            EncodeIOA(frame, 0);

            frame.SetNextByte(0xcc);
            frame.SetNextByte(0x55);

            if (debugOutput)
                Console.WriteLine("Encoded C_TS_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// 复位进程命令Sends a reset process command (C_RP_NA_1 typeID: 105).
        /// <para>1. 必须慎重使用复位进程命令</para>
        /// <para>2. 需要通信双方技术人员交流并去人复位条件方可进行</para>
        /// </summary>
        /// <param name="cot">
        /// 传送原因Cause of transmission
        /// <para>控制方向</para>
        /// <para>  6  激活</para>
        /// <para>监视方向</para>
        /// <para>  7  激活确认</para>
        /// <para>  10  激活终止</para>
        /// <para>  44  未知的类型标识</para>
        /// <para>  45  未知的传送原因</para>
        /// <para>  46  未知的应用服务数据单元公共地址 cot</para>
        /// <para>  47  未知的信息对象地址</para>
        /// </param>
        /// <param name="ca">公共地址Common address</param>
        /// <param name="qrp">
        /// 复位进程命令限定词Qualifier of reset process command
        /// <para>0：  未用</para>
        /// <para>1：  进程的总复位</para>
        /// <para>2：  复位时间缓冲区等待处理的带时标信息</para>
        /// </param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendResetProcessCommand(CauseOfTransmission cot, int ca, byte qrp)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_RP_NA_1, 1, cot, ca);

            EncodeIOA(frame, 0);

            frame.SetNextByte(qrp);

            if (debugOutput)
                Console.WriteLine("Encoded C_RP_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }


        /// <summary>
        /// Sends a delay acquisition command (C_CD_NA_1 typeID: 106).
        /// </summary>
        /// <param name="cot">Cause of transmission</param>
        /// <param name="ca">Common address</param>
        /// <param name="delay">delay for acquisition</param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendDelayAcquisitionCommand(CauseOfTransmission cot, int ca, CP16Time2a delay)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, TypeID.C_CD_NA_1, 1, cot, ca);

            EncodeIOA(frame, 0);

            frame.AppendBytes(delay.GetEncodedValue());

            if (debugOutput)
                Console.WriteLine("Encoded C_CD_NA_1 with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// 发送控制命令Sends the control command.
        /// </summary>
        /// 
        /// The type ID has to match the type of the InformationObject!
        /// 
        /// C_SC_NA_1 -> SingleCommand                  单位遥控命令
        /// C_DC_NA_1 -> DoubleCommand                  双位遥控命令
        /// C_RC_NA_1 -> StepCommand                    档位调节命令
        /// C_SC_TA_1 -> SingleCommandWithCP56Time2a    
        /// C_SE_NA_1 -> SetpointCommandNormalized      归一化设定值
        /// C_SE_NB_1 -> SetpointCommandScaled          标度化设定值
        /// C_SE_NC_1 -> SetpointCommandShort           短浮点设定值
        /// C_BO_NA_1 -> Bitstring32Command
        /// 
        /// 
        /// <param name="typeId">类型标识Type ID of the control command</param>
        /// <param name="cot">传送原因Cause of transmission (use ACTIVATION to start a control sequence)</param>
        /// <param name="ca">公共地址Common address</param>
        /// <param name="sc">信息体对象Information object of the command</param>
        /// <exception cref="ConnectionException">description</exception>
        public void SendControlCommand(TypeID typeId, CauseOfTransmission cot, int ca, InformationObject sc)
        {
            Frame frame = new T104Frame();

            EncodeIdentificationField(frame, typeId, 1 /* SQ:false; NumIX:1 */, cot, ca);

            sc.Encode(frame, parameters, false);

            if (debugOutput)
                Console.WriteLine("Encoded " + typeId.ToString() + " with " + frame.GetMsgSize() + " bytes.");

            sendIMessage(frame);
        }

        /// <summary>
        /// Start data transmission on this connection
        /// </summary>
        public void SendStartDT()
        {
            if (running)
            {
                socket.Send(STARTDT_ACT_MSG);
            }
            else
            {
                if (lastException != null)
                    throw new ConnectionException(lastException.Message, lastException);
                else
                    throw new ConnectionException("not connected", new SocketException(10057));
            }
        }

        /// <summary>
        /// Stop data transmission on this connection
        /// </summary>
        public void SendStopDT()
        {
            if (running)
            {
                socket.Send(STOPDT_ACT_MSG);
            }
            else
            {
                if (lastException != null)
                    throw new ConnectionException(lastException.Message, lastException);
                else
                    throw new ConnectionException("not connected", new SocketException(10057));
            }
        }

        /// <summary>
        /// 连接Connect this instance.
        /// <para>如果连接被拒或者超时，会抛异常，注意接着点。。。</para>
        /// <para>如果正在连接，或者已经连接上，也会抛异常，注意接着点。。。</para>
        /// </summary>
        /// 
        /// The function will throw a SocketException if the connection attempt is rejected or timed out.
        /// <exception cref="ConnectionException">description</exception>
        public void Connect()
        {
            ConnectAsync();

            while ((running == false) && (socketError == false))
            {
                Thread.Sleep(1);
            }

            if (socketError)
                throw new ConnectionException(lastException.Message, lastException);
        }

        /// <summary>
        /// Connects to the server (outstation). This is a non-blocking call. Before using the connection
        /// you have to check if the connection is already connected and running.
        /// </summary>
        /// <exception cref="ConnectionException">description</exception>
        public void ConnectAsync()
        {
            if ((running == false) && (connecting == false))
            {
                ResetConnection();

                Thread workerThread = new Thread(HandleConnection);

                workerThread.Start();
            }
            else
            {
                if (running)
                    throw new ConnectionException("already connected", new SocketException(10056)); /* WSAEISCONN - Socket is already connected */
                else
                    throw new ConnectionException("already connecting", new SocketException(10037)); /* WSAEALREADY - Operation already in progress */

            }
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="buffer">待接收数据缓冲，定义长点把。255以上</param>
        /// <returns>返回已接收长度</returns>
        private int receiveMessage(Socket socket, byte[] buffer)
        {
            // wait for first byte
            if (socket.Receive(buffer, 0, 1, SocketFlags.None) != 1)
                return 0;

            //判断是不是68开头
            if (buffer[0] != 0x68)
            {
                if (debugOutput)
                    Console.WriteLine("Missing SOF indicator!");

                return 0;
            }

            // read length byte
            if (socket.Receive(buffer, 1, 1, SocketFlags.None) != 1)
                return 0;

            int length = buffer[1];

            // read remaining frame 这里他直接读接下来所有的字节，如果传送把一个报文分开了。这里就sb了。。标个TODO吧。。。
            if (socket.Receive(buffer, 2, length, SocketFlags.None) != length)
            {
                if (debugOutput)
                    Console.WriteLine("Failed to read complete frame!");

                return 0;
            }

            return length + 2;
        }

        /// <summary>
        /// 检查t2超时（接收方无数据报文的确认超时时间）
        /// </summary>
        /// <param name="currentTime">当前时间</param>
        /// <returns>返回是否超时</returns>
        private bool checkConfirmTimeout(long currentTime)
        {
            if ((currentTime - lastConfirmationTime) >= (parameters.T2 * 1000))
                return true;
            else
                return false;
        }

        /// <summary>
        /// 信息检查，按说每收到一条信息都要来这里检查。。。
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="buffer"></param>
        /// <param name="msgSize"></param>
        /// <returns></returns>
        private bool checkMessage(Socket socket, byte[] buffer, int msgSize)
        {
            //这里检测I帧和U帧，不知道为什么不检测S帧，难道是S帧已经处理过了？
            //update：似乎他这里只需要发送S帧，不主动监测S帧。。。
            if ((buffer[2] & 1) == 0)
            { /* I format frame */

                if (debugOutput)
                    Console.WriteLine("Received I frame");

                if (msgSize < 7)
                {

                    if (debugOutput)
                        Console.WriteLine("I msg too small!");

                    return false;
                }

                receiveCount++;
                unconfirmedMessages++;

                long currentTime = SystemUtils.currentTimeMillis();

                if ((unconfirmedMessages > parameters.W) || checkConfirmTimeout(currentTime))
                {
                    //未确认消息超过W，或者t2超时，则发送S帧
                    lastConfirmationTime = currentTime;

                    unconfirmedMessages = 0;
                    sendSMessage();
                }

                //新建一个ASDU，准备解析
                ASDU asdu = new ASDU(parameters, buffer, msgSize);

                //解析ASDU
                if (asduReceivedHandler != null)
                    asduReceivedHandler(asduReceivedHandlerParameter, asdu);
            }
            else if ((buffer[2] & 0x03) == 0x03)
            { /* U format frame */

                if (buffer[2] == 0x43)
                { // Check for TESTFR_ACT message
                    //收到了测试命令，回复测试确认
                    socket.Send(TESTFR_CON_MSG);
                }
                else if (buffer[2] == 0x07)
                { /* STARTDT ACT */
                    //收到了开启命令，回复开启命令确认
                    socket.Send(STARTDT_CON_MSG);
                }
                else if (buffer[2] == 0x0b)
                { /* STARTDT_CON */
                    //收到了开启确认，处理[已连接]事件
                    if (connectionHandler != null)
                        connectionHandler(connectionHandlerParameter, ConnectionEvent.STARTDT_CON_RECEIVED);

                }
                else if (buffer[2] == 0x23)
                { /* STOPDT_CON */
                    //收到了停止确认，处理[已断开]事件
                    if (connectionHandler != null)
                        connectionHandler(connectionHandlerParameter, ConnectionEvent.STOPDT_CON_RECEIVED);
                }

            }

            return true;
        }

        private void ConnectSocketWithTimeout()
        {
            IPAddress ipAddress = IPAddress.Parse(hostname);
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, tcpPort);

            // Create a TCP/IP  socket.
            socket = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            var result = socket.BeginConnect(remoteEP, null, null);

            bool success = result.AsyncWaitHandle.WaitOne(connectTimeoutInMs, true);
            if (success)
            {
                socket.EndConnect(result);
            }
            else
            {
                socket.Close();
                throw new SocketException(10060); // Connection timed out.
            }
        }

        private void HandleConnection()
        {

            byte[] bytes = new byte[300];


            try
            {

                try
                {

                    connecting = true;

                    try
                    {
                        // Connect to a remote device.
                        ConnectSocketWithTimeout();

                        if (debugOutput)
                            Console.WriteLine("Socket connected to {0}",
                                socket.RemoteEndPoint.ToString());

                        //自动启动的话，直接发送开启命令了。。
                        if (autostart)
                            socket.Send(STARTDT_ACT_MSG);

                        running = true;
                        socketError = false;
                        connecting = false;

                        if (connectionHandler != null)
                            connectionHandler(connectionHandlerParameter, ConnectionEvent.OPENED);

                    }
                    catch (SocketException se)
                    {
                        if (debugOutput)
                            Console.WriteLine("SocketException : {0}", se.ToString());

                        running = false;
                        socketError = true;
                        lastException = se;
                    }

                    bool loopRunning = running;

                    while (loopRunning)
                    {

                        try
                        {
                            // Receive a message from from the remote device.
                            int bytesRec = receiveMessage(socket, bytes);

                            if (bytesRec > 0)
                            {

                                if (debugOutput)
                                    Console.WriteLine(
                                        BitConverter.ToString(bytes, 0, bytesRec));

                                //TODO call raw message handler if available

                                if (checkMessage(socket, bytes, bytesRec) == false)
                                {
                                    /* close connection on error */
                                    loopRunning = false;
                                }
                            }
                            else
                                loopRunning = false;
                        }
                        catch (SocketException)
                        {
                            loopRunning = false;
                        }
                    }

                    if (debugOutput)
                        Console.WriteLine("CLOSE CONNECTION!");

                    // Release the socket.
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();

                    if (connectionHandler != null)
                        connectionHandler(connectionHandlerParameter, ConnectionEvent.CLOSED);

                }
                catch (ArgumentNullException ane)
                {
                    connecting = false;
                    if (debugOutput)
                        Console.WriteLine("ArgumentNullException : {0}", ane.ToString());
                }
                catch (SocketException se)
                {
                    if (debugOutput)
                        Console.WriteLine("SocketException : {0}", se.ToString());
                }
                catch (Exception e)
                {
                    if (debugOutput)
                        Console.WriteLine("Unexpected exception : {0}", e.ToString());
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            running = false;
            connecting = false;
        }

        public bool IsRunning
        {
            get
            {
                return this.running;
            }
        }

        /// <summary>
        /// 关闭连接（带阻塞）
        /// </summary>
        public void Close()
        {
            if (running)
            {
                socket.Shutdown(SocketShutdown.Both);

                while (running)
                    Thread.Sleep(1);
            }
        }

        /// <summary>
        /// 设置ASDU解析代理和参数
        /// </summary>
        /// <param name="handler">解析代理</param>
        /// <param name="parameter">解析参数</param>
        public void SetASDUReceivedHandler(ASDUReceivedHandler handler, object parameter)
        {
            asduReceivedHandler = handler;
            asduReceivedHandlerParameter = parameter;
        }

        /// <summary>
        /// Sets the connection handler. The connection handler is called when
        /// the connection is established or closed
        /// <para>设置连接处理句柄，当连接断开或者已连接时，会调用此处理句柄</para>
        /// </summary>
        /// <param name="handler">被调用的句柄the handler to be called</param>
        /// <param name="parameter">调用参数user provided parameter that is passed to the handler，主要是已断开/已连接两个参数，参见【ConnectionEvent】</param>
        public void SetConnectionHandler(ConnectionHandler handler, object parameter)
        {
            connectionHandler = handler;
            connectionHandlerParameter = parameter;
        }

    }
}

