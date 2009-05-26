﻿/* 
 * nMQTT, a .Net MQTT v3 client implementation.
 * http://code.google.com/p/nmqtt
 * 
 * Copyright (c) 2009 Mark Allanson (mark@markallanson.net)
 *
 * Licensed under the MIT License. You may not use this file except 
 * in compliance with the License. You may obtain a copy of the License at
 *
 *     http://www.opensource.org/licenses/mit-license.php
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Nmqtt
{
    /// <summary>
    /// Abstract base that provides shared connection functionality to connection handler impementations.
    /// </summary>
    internal abstract class MqttConnectionHandler : IMqttConnectionHandler
    {
        protected MqttConnection connection;
        
        /// <summary>
        /// Registry of message processors
        /// </summary>
        private Dictionary<MqttMessageType, List<Func<MqttMessage, bool>>> messageProcessorRegistry = new Dictionary<MqttMessageType, List<Func<MqttMessage, bool>>>();
        
        /// <summary>
        /// Registry of sent message callbacks
        /// </summary>
        private List<Func<MqttMessage, bool>> sentMessageCallbacks = new List<Func<MqttMessage,bool>>();

        protected ConnectionState connectionState = ConnectionState.Disconnected;

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttConnectionHandler"/> class.
        /// </summary>
        public MqttConnectionHandler()
        {
            // pre-prepare the message processor registry
            foreach (MqttMessageType type in Enum.GetValues(typeof(MqttMessageType)))
            {
                messageProcessorRegistry.Add(type, new List<Func<MqttMessage,bool>>());
            }
        }

        /// <summary>
        /// Gets the current conneciton state of the Mqtt Client.
        /// </summary>
        public ConnectionState State
        {
            get
            {
                return connectionState;
            }
        }

        /// <summary>
        /// Connect to the specific Mqtt Connection.
        /// </summary>
        /// <param name="server">The server to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        /// <param name="message">The connect message to use as part of the connection process.</param>
        public ConnectionState Connect(string server, int port, MqttConnectMessage message)
        {
            ConnectionState state = InternalConnect(server, port, message);

            // if we managed to connection, ensure we catch any unexpected disconnects
            if (state == ConnectionState.Connected)
            {
                this.connection.ConnectionDropped += (sender, e) => { this.connectionState = ConnectionState.Disconnected; };
            }

            return state;
        }

        /// <summary>
        /// Connect to the specific Mqtt Connection.
        /// </summary>
        /// <param name="server">The server to connect to.</param>
        /// <param name="port">The port to connect to.</param>
        /// <param name="message">The connect message to use as part of the connection process.</param>
        protected abstract ConnectionState InternalConnect(string server, int port, MqttConnectMessage message);

        /// <summary>
        /// Sends a message to the broker through the current connection.
        /// </summary>
        /// <param name="message"></param>
        public void SendMessage(MqttMessage message)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                message.WriteTo(stream);
                stream.Seek(0, SeekOrigin.Begin);
                connection.Send(stream);
            }

            // let any registered people know we're doing a message.
            foreach (Func<MqttMessage, bool> callback in sentMessageCallbacks)
            {
                callback(message);
            }
        }

        /// <summary>
        /// Runs the disconnection process to stop communicating with a message broker.
        /// </summary>
        /// <returns></returns>
        protected abstract ConnectionState Disconnect();

        /// <summary>
        /// Closes the connection to the Mqtt message broker.
        /// </summary>
        public void Close()
        {
            if (connectionState == ConnectionState.Connecting)
            {
                // TODO: Decide what to do if the caller tries to close a connection that is in the process of being connected.
            }

            if (connectionState == ConnectionState.Connected)
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Registers for the receipt of messages when they arrive.
        /// </summary>
        /// <param name="msgType">The message type to register for.</param>
        /// <param name="msgProcessor">The callback function that will be executed when the message arrives.</param>
        public void RegisterForMessage(MqttMessageType msgType, Func<MqttMessage, bool> msgProcessorCallback)
        {
            messageProcessorRegistry[msgType].Add(msgProcessorCallback);
        }

        /// <summary>
        /// UnRegisters for the receipt of messages when they arrive.
        /// </summary>
        /// <param name="msgType">The message type to register for.</param>
        /// <param name="msgProcessorCallback">The MSG processor callback.</param>
        public void UnRegisterForMessage(MqttMessageType msgType, Func<MqttMessage, bool> msgProcessorCallback)
        {
            messageProcessorRegistry[msgType].Remove(msgProcessorCallback);
        }

        /// <summary>
        /// Registers a callback to be called whenever a message is sent.
        /// </summary>
        /// <param name="sentMsgCallback">The sent MSG callback.</param>
        public void RegisterForAllSentMessages(Func<MqttMessage, bool> sentMsgCallback)
        {
            sentMessageCallbacks.Add(sentMsgCallback);
        }

        /// <summary>
        /// UnRegisters a callback that is called whenever a message is sent.
        /// </summary>
        /// <param name="sentMsgCallback">The sent MSG callback.</param>
        public void UnRegisterForAllSentMessages(Func<MqttMessage, bool> sentMsgCallback)
        {
            sentMessageCallbacks.Remove(sentMsgCallback);
        }

        /// <summary>
        /// Handles the DataAvailable event of the connection control for handling non connection messages
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Nmqtt.DataAvailableEventArgs"/> instance containing the event data.</param>
        protected void connection_MessageDataAvailable(object sender, DataAvailableEventArgs e)
        {
            try
            {
                // read the message, and if it's valid, signal to the keepalive so that we don't
                // spam ping requests at the broker.
                MqttMessage msg = MqttMessage.CreateFrom(e.MessageData);

                List<Func<MqttMessage, bool>> callbacks = messageProcessorRegistry[msg.Header.MessageType];
                foreach (Func<MqttMessage, bool> callback in callbacks)
                {
                    callback(msg);
                }
            }
            catch (InvalidMessageException)
            {
                // TODO: Figure out what to do if we receive a dud message during normal processing.
            }
        }

        #region IDisposable Members

        /// <summary>
        /// Cleans up the underlying raw connection to the server.
        /// </summary>
        public void Dispose()
        {
            if (connection != null)
            {
                this.Close();
                connection.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}