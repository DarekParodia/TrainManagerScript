using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        private string trainNameMOSI = "train_1_mosi"; // ID used to transmit IGC messages from host to client
        private string trainNameMISO = "train_1_miso"; // ID used to receive IGC messages from client to host
        private string thrusterForwardGroup = "CargoOne.ThrusterForward"; // Group containing thrusters that will be used for going forward
        private string thrusterBackwardGroup = "CargoOne.ThrusterBackward"; // Group containing thrusters that will be used for going backward
        
        IMyBroadcastListener _broadcastReceiver;
        
        List<IMyThrust> thrusterForward = new List<IMyThrust>();
        List<IMyThrust> thrusterBackward = new List<IMyThrust>();

        private int lastSPeedCounter = 0;
        private int maxSpeedSafetyTicks = 5; // after how many ticks stop thrusters if no new speed message is received
        private enum dataType : byte
        {
            PING = 0,
            SPEED = 1
        }
        private struct message
        {
            public string cargoID;
            public dataType type;
            public string data;
        }

        public Program()
        {
            Echo("Starting Train Client...");
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            _broadcastReceiver = IGC.RegisterBroadcastListener(trainNameMOSI);
            _broadcastReceiver.SetMessageCallback(trainNameMOSI);
            
            // set thrusters
            List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
            GridTerminalSystem.GetBlockGroups(groups);
            foreach (IMyBlockGroup group in groups)
            {
                if (group.Name == thrusterForwardGroup)
                {
                    group.GetBlocksOfType(thrusterForward);
                }
                else if (group.Name == thrusterBackwardGroup)
                {
                    group.GetBlocksOfType(thrusterBackward);
                }
            }
            Echo("Train Client");
        }
        
        private void sendToMaster(dataType type, string data)
        {
            Echo("Sending to master: " + (byte)type + " " + data);
            IGC.SendBroadcastMessage(trainNameMISO, Me.CubeGrid.CustomName + ";" + (byte)type + ";" + data);
        }
        private message parseIGCMessage(MyIGCMessage myIGCMessage)
        {
            message msg = new message();
            string[] data = myIGCMessage.Data.ToString().Split(';');
            msg.cargoID = data[0];
            msg.type = (dataType)Enum.Parse(typeof(dataType), data[1]);
            // rest goes to data even if there are more than 3 elements
            for (int i = 2; i < data.Length; i++)
            {
                msg.data += data[i];
                if (i < data.Length - 1)
                    msg.data += ";";
            }
            return msg;
        }
        private void parsePingMessage(message msg)
        {
            Vector3 position = Me.GetPosition();
            sendToMaster(dataType.PING, "pong");
        }
        private void parseSpeedMessage(message msg)
        {
            Echo("data: " + msg.data);
            
            string[] data = msg.data.Split(';');
            float z = float.Parse(data[2]);
            
            // set thrusters
            setThrusters(z);
            lastSPeedCounter = 0;
        }

        private void setThrusters(float thrust)
        {
            if (thrust == 0)
            {
                foreach (IMyThrust thruster in thrusterForward)
                {
                    thruster.ThrustOverridePercentage = 0;
                }

                foreach (IMyThrust thruster in thrusterBackward)
                {
                    thruster.ThrustOverridePercentage = 0;
                }
            }
            else if (thrust > 0)
            {
                foreach (IMyThrust thruster in thrusterForward)
                {
                    thruster.ThrustOverridePercentage = 0;
                }
                foreach (IMyThrust thruster in thrusterBackward)
                {
                    thruster.ThrustOverridePercentage = thrust;
                }
            }
            else
            {
                foreach (IMyThrust thruster in thrusterForward)
                {
                    thruster.ThrustOverridePercentage = -thrust;
                }
                foreach (IMyThrust thruster in thrusterBackward)
                {
                    thruster.ThrustOverridePercentage = 0;
                }
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                lastSPeedCounter++;
                if (lastSPeedCounter >= maxSpeedSafetyTicks)
                {
                    setThrusters(0);
                }

                while (_broadcastReceiver.HasPendingMessage)
                {
                    MyIGCMessage myIGCMessage = _broadcastReceiver.AcceptMessage();
                    message msg = this.parseIGCMessage(myIGCMessage);

                    if (msg.cargoID != Me.CubeGrid.CustomName &&
                        msg.cargoID != "all") // skip if message is not for this cargo
                        continue;

                    switch (msg.type)
                    {
                        case dataType.PING:
                            parsePingMessage(msg);
                            break;
                        case dataType.SPEED:
                            parseSpeedMessage(msg);
                            break;
                    }
                }
            } catch (Exception e)
            {
                Echo("Error: " + e.Message);
                setThrusters(0); // aditional safety if something crashes
            }
        }
    }
}