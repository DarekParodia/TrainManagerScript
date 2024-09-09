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
        private string trainNameMOSI = "train_1_mosi";   // ID used to transmit IGC messages from host to client
        private string trainNameMISO = "train_1_miso";   // ID used to receive IGC messages from client to host
        private string cockpitStringTag = "[TrainHost]"; // Tag used to identify the cockpit (can be remote controller)
        
        IMyShipController cockpit;
        IMyBroadcastListener _broadcastReceiver;
        
        private mode currentMode = mode.AUTO;
        private struct waypoint
        {
            public Vector3 position;
            public float speed;
            public string name;
        }
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
            public float mass; // Total Mass of cargo from Sandbox.ModAPI.Ingame.MyShipMass
        }
        private enum mode
        {
            AUTO,
            MANUAL
        }
        private enum dataType : byte
        {
            PING = 0,
            SPEED = 1,
            DAMPENERS = 2
        }

        private List<cargo> cargo_list = new List<cargo>();
        private List<waypoint> Waypoints = new List<waypoint>(); // Add waypoints trough custom data. Format: just copy paste the coordinates from GPS + :speed
        
        private int currentWaypoint = 0;
        private bool goUpList = true; // if true, go up the list of waypoints, if false go down
        private int distanceThreshold = 10; // distance threshold to consider waypoint reached
        
        int counter = 0;
        
        public Program()
        {
            Echo("Starting Train Host...");
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            
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
            
            // append waypoints from custom data
            string customData = Me.CustomData;
            string[] waypointsData = customData.Split('\n');
            foreach (string waypointData in waypointsData)
            {
                string[] waypointDataSplit = waypointData.Split(':');
                waypoint newWaypoint = new waypoint();
                newWaypoint.position = new Vector3(float.Parse(waypointDataSplit[2]), float.Parse(waypointDataSplit[3]), float.Parse(waypointDataSplit[4]));
                newWaypoint.speed = float.Parse(waypointDataSplit[6]);
                newWaypoint.name = waypointDataSplit[1];
                this.Waypoints.Add(newWaypoint);
            }
            
            // log waypoints
            Echo("Waypoint Count: " + this.Waypoints.Count);
            Echo("Waypoints Initialized: ");
            foreach (waypoint wp in this.Waypoints)
            {
                Echo(wp.position.ToString() + " " + wp.speed);
            }
    
            Vector3 currentPosition = cockpit.GetPosition();
            this.currentWaypoint = getClosestWaypoint(currentPosition);

            Echo("Train Host Active");
        }
        private int getClosestWaypoint(Vector3 currentPosition)
        {
            int closestWaypoint = 0;
            float closestDistance = float.MaxValue;
            for (int i = 0; i < this.Waypoints.Count; i++)
            {
                float distance = Vector3.Distance(currentPosition, this.Waypoints[i].position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestWaypoint = i;
                }
            }
            return closestWaypoint;
        }
        
        private bool hasReachedWaypoint(Vector3 currentPosition, Vector3 targetPosition)
        {
            return Vector3.Distance(currentPosition, targetPosition) < distanceThreshold;
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
            
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        }

        private void sendSpeed(string cargoID, Vector3 speed)
        {
            string speedString = speed.X + ";" + speed.Y + ";" + speed.Z;
            this.TransmitData(cargoID, dataType.SPEED, speedString);
        }
        private void sendDampeners(string cargoID, bool dampeners)
        {
            string dampenersString = dampeners.ToString();
            this.TransmitData(cargoID, dataType.DAMPENERS, dampenersString);
        }
        public void AutoMode(string argument)
        {
            if (this.Waypoints.Count < 2)
            {
                Echo("Not enough waypoints, at least 2 needed");
                return;
            }
                
            Vector3 currentPosition = cockpit.GetPosition();
            
            if (hasReachedWaypoint(currentPosition, Waypoints[currentWaypoint + (goUpList ? 1 : -1)].position))
            {
                if (goUpList)
                {
                    currentWaypoint++;
                    if (currentWaypoint == Waypoints.Count - 1)
                    {
                        goUpList = false;
                    }
                }
                else
                {
                    currentWaypoint--;
                    if (currentWaypoint == 0)
                    {
                        goUpList = true;
                    }
                }
            }
            
            Echo("Current Waypoint: " + Waypoints[currentWaypoint].name);
            Echo("Direction: " + (goUpList ? "uplist" : "downlist"));
            Echo("Target Waypoint: " + Waypoints[currentWaypoint + (goUpList ? 1 : -1)].name);
            // distance to target
            Vector3 targetPosition = Waypoints[currentWaypoint + (goUpList ? 1 : -1)].position;
            float distance = Vector3.Distance(currentPosition, targetPosition);
            Echo("Distance to target: " + distance);
            
            // calculate speed limit
            float speedLimit = Waypoints[currentWaypoint].speed;
            if (!goUpList)
            {
                speedLimit = Waypoints[currentWaypoint - 1].speed;
            }
            
            Echo("Current Speed Limit: " + speedLimit);
        }
        
        public void ManualMode(string argument)
        {
            // check if player is trying to move the train
            Vector3 MoveIndicator = cockpit.MoveIndicator;
            this.sendSpeed("all", MoveIndicator);
            
            // check if dampeners are on
            bool dampeners = cockpit.DampenersOverride;
            this.sendDampeners("all", dampeners);
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