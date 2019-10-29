﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using YeeLightAPI.YeeLightConstants;
using YeeLightAPI.YeeLightExceptions;

namespace YeeLightAPI
{
    public class YeeLightDevice
    {
        private IPAddress lightIpAddress = null;
        private ushort lightPort = 0;
        private TcpClient yeelightTcpClient = new TcpClient();
        private bool musicMode = false;
        private Exception lastError = null;

        public YeeLightDevice() { }

        public YeeLightDevice(IPAddress ipAddress, ushort port = Constants.DefaultCommandPort)
        {
            lightIpAddress = ipAddress;
            lightPort = port;
        }

        /// <exception cref="Exception"></exception>
        public YeeLightDevice(string hostname, ushort port = Constants.DefaultCommandPort)
        {
            if (!IPAddress.TryParse(hostname, out lightIpAddress))
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(hostname);
                if (hostEntry.AddressList.Length > 0)
                {
                    lightIpAddress = hostEntry.AddressList[0];
                }
                else
                {
                    throw new ArgumentException();
                }
            }
            lightPort = port;
        }

        public bool Connect()
        {
            try
            {
                yeelightTcpClient.Connect(lightIpAddress, lightPort);
                musicMode = false;
                return true;
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        public (IPAddress ipAddress, ushort port) GetLightIPAddressAndPort()
        {
            return (ipAddress: lightIpAddress, port: lightPort);
        }

        public void SetLightIPAddressAndPort(IPAddress ipAddress, ushort port)
        {
            lightIpAddress = ipAddress;
            lightPort = port;
        }

        public bool IsConnected()
        {
            return yeelightTcpClient.Connected;
        }

        public bool IsMusicMode()
        {
            return musicMode && IsConnected();
        }

        public Exception GetLastError()
        {
            return lastError;
        }

        public bool CloseConnection()
        {
            try
            {
                ThrowExceptionIfNotConnected();
                yeelightTcpClient.GetStream().Close();
                yeelightTcpClient.Close();

                yeelightTcpClient = new TcpClient();
                return true;
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        public bool SetBrightness(int brightness, //Range is 0 - 100
            int duration = Constants.MinValueForDurationParameter,
            Constants.EffectParamValues effectType = Constants.EffectParamValues.SUDDEN)
        {
            try
            {
                if (brightness > 100 || brightness < 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                ThrowExceptionIfNotConnected();
                return SendCommandMessage(1, "set_bright", new string[] { brightness.ToString(), Utils.GetJsonStringFromParamEnum(effectType), duration.ToString() });
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        public bool SetPower(Constants.PowerStateParamValues powerState,
            int duration = Constants.MinValueForDurationParameter,
            Constants.EffectParamValues effectType = Constants.EffectParamValues.SUDDEN)
        {
            try
            {
                ThrowExceptionIfNotConnected();
                return SendCommandMessage(1, "set_power",
                    new string[] { Utils.GetJsonStringFromParamEnum(powerState), Utils.GetJsonStringFromParamEnum(effectType), duration.ToString() });
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        public bool SetColor(int red, int green, int blue,
            int duration = Constants.MinValueForDurationParameter,
            Constants.EffectParamValues effectType = Constants.EffectParamValues.SUDDEN)
        {
            try
            {
                if (duration < Constants.MinValueForDurationParameter)
                {
                    throw new ArgumentOutOfRangeException();
                }
                ThrowExceptionIfNotConnected();
                int value = ((red) << 16) | ((green) << 8) | (blue);
                effectType.ToString();
                return SendCommandMessage(1, "set_rgb", new string[] { value.ToString(), Utils.GetJsonStringFromParamEnum(effectType), duration.ToString() });
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        public bool SetMusicMode(IPAddress localIP, ushort localPort, bool state)
        {
            try
            {
                if (state)
                {
                    ThrowExceptionIfNotConnected();
                    ThrowExceptionIfInMusicMode();
                    TcpListener musicModeTcpListener = new TcpListener(localIP, localPort);
                    musicModeTcpListener.Start();
                    SendCommandMessage(1, "set_music", new string[] { "1", $"\"{localIP}\"", $"{localPort}" });
                    yeelightTcpClient.GetStream().Close();
                    yeelightTcpClient.Close();

                    int i = 0;
                    while (!musicModeTcpListener.Pending())
                    {
                        Thread.Sleep(100);
                        if (i >= 10)
                        {
                            musicModeTcpListener.Stop();
                            throw new TimeoutException();
                        }
                        i++;
                    }
                    yeelightTcpClient = musicModeTcpListener.AcceptTcpClient();
                    musicModeTcpListener.Stop();
                    musicMode = true;
                }
                else
                {
                    if (IsConnected())
                    {
                        SendCommandMessage(1, "set_music", new string[] { "0" });
                    }
                    yeelightTcpClient.GetStream().Close();
                    yeelightTcpClient.Close();
                    yeelightTcpClient.Connect(lightIpAddress, lightPort);
                    musicMode = false;
                }
                ThrowExceptionIfNotConnected();
                return true;
            }
            catch (Exception exc) { lastError = exc; }
            return false;
        }

        private void ThrowExceptionIfNotConnected()
        {
            if (!IsConnected())
            {
                throw new Exceptions.DeviceIsNotConnected();
            }
        }
        private void ThrowExceptionIfInMusicMode()
        {
            if (!IsMusicMode())
            {
                throw new Exceptions.DeviceIsAlreadyInMusicMode();
            }
        }
        private bool SendCommandMessage(int id_pair, string method_pair, string[] params_pair)
        {
            //TODO: use proper json serializer library here
            string commandMessage = $"{{\"id\":{id_pair},\"method\":\"{method_pair}\",\"params\":[{string.Join(",", params_pair)}]}}\r\n";
            return SendString(commandMessage);
        }
        private bool SendString(string command)
        {
            ThrowExceptionIfNotConnected();

            byte[] bytesOfCommand = Encoding.ASCII.GetBytes(command);
            return yeelightTcpClient.Client.Send(bytesOfCommand) == bytesOfCommand.Length;
        }

    }
}