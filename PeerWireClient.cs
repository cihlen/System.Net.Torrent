﻿/*
Copyright (c) 2013, Darren Horrocks
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice, this
  list of conditions and the following disclaimer in the documentation and/or
  other materials provided with the distribution.

* Neither the name of the {organization} nor the names of its
  contributors may be used to endorse or promote products derived from
  this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE. 
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace System.Net.Torrent
{
    public class PeerWireClient
    {
        private readonly byte[] _bitTorrentProtocolHeader = new byte[] { 0x42, 0x69, 0x74, 0x54, 0x6F, 0x72, 0x72, 0x65, 0x6E, 0x74, 0x20, 0x70, 0x72, 0x6F, 0x74, 0x6F, 0x63, 0x6F, 0x6C };

        private readonly TcpClient _client;
        private byte[] _internalBuffer;

        public Int32 Timeout { get; private set; }
        public bool[] BitField { get; set; }

        private DateTime _lastKeepAlive;
        public bool KeepAlive { get; set; }

        public PeerWireClient(Int32 timeout)
        {
            Timeout = timeout;
            _client = new TcpClient
            {
                Client =
                {
                    ReceiveTimeout = timeout*1000,
                    SendTimeout = timeout*1000
                }
            };
            _internalBuffer = new byte[0];
            KeepAlive = true;
        }

        public void Connect(IPEndPoint endPoint)
        {
            _client.Connect(endPoint);
        }

        public void Connect(String ipHost, Int32 port)
        {
            _client.Connect(ipHost, port);
        }

        public void Disconnect()
        {
            _client.Close();
        }

        public void Handshake(String hash, String peerId)
        {
            Handshake(Pack.Hex(hash), Encoding.ASCII.GetBytes(peerId));
        }

        public void Handshake(byte[] hash, byte[] peerId)
        {
            if (hash == null) throw new ArgumentNullException("hash", "Hash cannot be null");
            if (peerId == null) throw new ArgumentNullException("peerId", "Peer ID cannot be null");

            if (hash.Length != 20) throw new ArgumentOutOfRangeException("hash", "hash must be 20 bytes exactly");
            if (peerId.Length != 20) throw new ArgumentOutOfRangeException("peerId", "Peer ID must be 20 bytes exactly");

            byte[] sendBuf = (new [] { (byte) _bitTorrentProtocolHeader.Length }).Concat(_bitTorrentProtocolHeader).Concat(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }).Concat(hash).Concat(peerId).ToArray();

            _client.Client.Send(sendBuf);

            byte[] readBuf = new byte[68];
            _client.Client.Receive(readBuf);

            Int32 resLen = readBuf[0];
            if (resLen != 19)
            {
                
            }

            _lastKeepAlive = DateTime.Now;

            byte[] recBuffer = new byte[128];
            _client.Client.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public void OnReceived(IAsyncResult ar)
        {
            if (_client.Client == null) return;

            byte[] data = (byte[])ar.AsyncState;

            Int32 len = _client.Client.EndReceive(ar);

            lock (_internalBuffer) _internalBuffer = _internalBuffer == null ? data : _internalBuffer.Concat(data.Take(len)).ToArray();

            byte[] recBuffer = new byte[128];
            _client.Client.BeginReceive(recBuffer, 0, 128, SocketFlags.None, OnReceived, recBuffer);
        }

        public bool Process()
        {
            Thread.Sleep(1);

            if (_internalBuffer.Length < 4)
            {
                Thread.Sleep(10);
                return true;
            }

            Int32 commandLength = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);

            lock (_internalBuffer) _internalBuffer = _internalBuffer.Skip(4).ToArray();

            if (commandLength == 0)
            {
                if (KeepAlive) _client.Client.Send(Pack.Int32(0));
                return true;
            }

            Int32 commandId = _internalBuffer[0];

            lock (_internalBuffer) _internalBuffer = _internalBuffer.Skip(1).ToArray();

            switch (commandId)
            {
                case 0:
                    //choke
                    break;
                case 1:
                    //unchoke
                    break;
                case 2:
                    //interested
                    break;
                case 3:
                    //not interested
                    break;
                case 4:
                    //have
                    ProcessHave();
                    break;
                case 5:
                    //bitfield
                    ProcessBitfield(commandLength-1);
                    break;
                case 6:
                    _internalBuffer = _internalBuffer.Skip(12).ToArray();
                    break;
                case 7:
                    _internalBuffer = _internalBuffer.Skip(commandLength-9).ToArray();
                    break;
                case 8:
                    _internalBuffer = _internalBuffer.Skip(12).ToArray();
                    break;
                case 9:
                    _internalBuffer = _internalBuffer.Skip(2).ToArray();
                    break;
                default:
                    break;
            }

            return true;
        }

        private void ProcessBitfield(Int32 length)
        {
            BitField = new bool[length * 8];
            for (int i = 0; i < length; i++)
            {
                byte b = _internalBuffer[0];

                BitField[(i * 8) + 0] = b.GetBit(0);
                BitField[(i * 8) + 1] = b.GetBit(1);
                BitField[(i * 8) + 2] = b.GetBit(2);
                BitField[(i * 8) + 3] = b.GetBit(3);
                BitField[(i * 8) + 4] = b.GetBit(4);
                BitField[(i * 8) + 5] = b.GetBit(5);
                BitField[(i * 8) + 6] = b.GetBit(6);
                BitField[(i * 8) + 7] = b.GetBit(7);

                lock (_internalBuffer) _internalBuffer = _internalBuffer.Skip(1).ToArray();
            }
        }

        private void ProcessHave()
        {
            Int32 pieceIndex = Unpack.Int32(_internalBuffer, 0, Unpack.Endianness.Big);
            BitField[pieceIndex] = true;
            lock (_internalBuffer) _internalBuffer = _internalBuffer.Skip(4).ToArray();
        }
    }
}
