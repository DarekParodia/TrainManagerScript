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
        private string cockpitStringTag = "[TrainHost]"; // Tag used to identify the cockpit
        
        IMyShipController cockpit;
        IMyBroadcastListener _broadcastReceiver;
        
        private mode currentMode = mode.MANUAL;
        
        private struct message
        {
            public string cargoID;
            public dataType type;
            public string data;
        }

        private struct cargo
        {
            public string cargoID;
            public Vector3 position;
        }
        private enum mode
        {
            AUTO,
            MANUAL
        }

        private enum dataType : byte
        {
            PING = 0,
            SPEED = 1
        }

        private List<cargo> cargo_list = new List<cargo>();
        int counter = 0;
        
        public Program()
        {
            Echo("Starting Train Host...");
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _broadcastReceiver = IGC.RegisterBroadcastListener(trainNameMISO);
            _broadcastReceiver.SetMessageCallback(trainNameMISO);
            
            // search for cockpit
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(blocks);
            foreach (IMyTerminalBlock block in blocks)
            {
                if (block.CustomName.Contains(cockpitStringTag))
                {
                    cockpit = block as IMyShipController;
                    break;
                }
            }

            if (cockpit == null)
            {
                throw new Exception("No cockpit found with tag: " + cockpitStringTag);
            }
            
            Echo("Train Host Active");
            this.ping();
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
            }
            return msg;
        }
        
        private void TransmitData(string cargoID, dataType type, string data)
        {
            IGC.SendBroadcastMessage(trainNameMOSI
                , cargoID + ";" + (byte)type + ";" + data);
        }

        private void ping()
        {
            this.TransmitData("all", dataType.PING, "ping");
            
            // receive ping
            this.cargo_list.Clear();

            while (_broadcastReceiver.HasPendingMessage)
            {
                MyIGCMessage myIGCMessage = _broadcastReceiver.AcceptMessage();
                message msg = parseIGCMessage(myIGCMessage);

                if (msg.type == dataType.PING)
                {
                    Echo("Ping received");
                    cargo newCargo = new cargo();
                    newCargo.cargoID = msg.cargoID;
                    this.cargo_list.Add(newCargo);
                }
                
            }
            
            Echo("Cargo list: ");
            foreach (cargo c in this.cargo_list)
            {
                Echo(c.cargoID);
            }
        }

        private void sendSpeed(string cargoID, Vector3 speed)
        {
            string speedString = speed.X + ";" + speed.Y + ";" + speed.Z;
            this.TransmitData(cargoID, dataType.SPEED, speedString);
        }

        public void AutoMode(string argument)
        {

        }
        
        public void ManualMode(string argument)
        {
            // check if player is trying to move the train
            Vector3 MoveIndicator = cockpit.MoveIndicator;
            this.sendSpeed("all", MoveIndicator);
        }

        public void Main(string argument, UpdateType updateSource)
        {
            switch (argument) // so that driver can switch between modes if needed
            {
                case "auto":
                    this.currentMode = mode.AUTO;
                    break;
                case "manual":
                    this.currentMode = mode.MANUAL;
                    break;
            }
            
            if (this.cargo_list.Count == 0)
            {
                this.ping();
                return;
            }
            
            switch (this.currentMode)
            {
                case mode.AUTO:
                    this.AutoMode(argument);
                    break;
                case mode.MANUAL:
                    this.ManualMode(argument);
                    break;
            }
        }
    }
}