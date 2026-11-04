// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PrimalEditor.Utilities;

class UdpLogReceiver
{
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _receiverTask;
    private int _isStopped;

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex)
            {
                Logger.Log(MessageType.Error, $"Error receiving UDP log message: {ex.Message}");
                break;
            }

            try
            {
                using var reader = new BinaryReader(new MemoryStream(result.Buffer));
                var message = ReadString(reader, sizeof(int) * 3);
                var file = ReadString(reader, sizeof(int) * 2);
                var line = reader.ReadInt32();
                var messageType = (MessageType)reader.ReadInt32();
                var processId = reader.ReadInt32();

                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("UDP log packet contains trailing data.");

                Logger.Log(messageType, message, processId, file, "", line);
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
            {
                Logger.Log(MessageType.Error, $"Malformed UDP log packet: {ex.Message}");
            }
        }
    }

    private static string ReadString(BinaryReader reader, int trailingBytes)
    {
        var length = reader.ReadInt32();
        var remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
        if (length < 0 || remainingBytes < trailingBytes || length > remainingBytes - trailingBytes)
            throw new InvalidDataException("UDP log packet contains an invalid string length.");

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();

        return Encoding.UTF8.GetString(bytes);
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isStopped, 1) != 0)
            return;

        _cancellation.Cancel();
        try
        {
            _receiverTask.GetAwaiter().GetResult();
        }
        finally
        {
            _client.Dispose();
            _cancellation.Dispose();
        }
    }

    public UdpLogReceiver(int port = 8080)
    {
        var endpoint = new IPEndPoint(IPAddress.Any, port);
        var groupAddress = IPAddress.Parse("239.0.0.222");

        _client = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.Client.Bind(endpoint);
        _client.JoinMulticastGroup(groupAddress);

        _receiverTask = ReceiveMessagesAsync(_cancellation.Token);
    }
}
