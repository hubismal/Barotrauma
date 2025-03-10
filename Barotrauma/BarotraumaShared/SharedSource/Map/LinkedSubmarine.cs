﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.IO;

namespace Barotrauma
{
    partial class LinkedSubmarinePrefab : MapEntityPrefab
    {
        //public static readonly PrefabCollection<LinkedSubmarinePrefab> Prefabs = new PrefabCollection<LinkedSubmarinePrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            //Prefabs.Remove(this);
        }

        public readonly SubmarineInfo subInfo;
        
        public LinkedSubmarinePrefab(SubmarineInfo subInfo)
        {
            this.subInfo = subInfo;
        }

        protected override void CreateInstance(Rectangle rect)
        {
            System.Diagnostics.Debug.Assert(Submarine.MainSub != null);
            LinkedSubmarine.CreateDummy(Submarine.MainSub, subInfo.FilePath, rect.Location.ToVector2());
        }
    }

    partial class LinkedSubmarine : MapEntity
    {
        private List<Vector2> wallVertices;

        private string filePath;

        private bool loadSub;
        public bool LoadSub => loadSub;
        private Submarine sub;

        private ushort originalMyPortID;

        //the ID of the docking port the sub was docked to in the original sub file
        //(needed when replacing a lost sub)
        private ushort originalLinkedToID;
        public ushort OriginalLinkedToID => originalLinkedToID;
        private DockingPort originalLinkedPort;

        private bool purchasedLostShuttles;

        public Submarine Sub
        {
            get
            {
                return sub;
            }
        }

        private XElement saveElement;

        public override bool Linkable
        {
            get
            {
                return true;
            }
        }
        
        public LinkedSubmarine(Submarine submarine, ushort id = Entity.NullEntityID)
            : base(null, submarine, id)
        {
            linkedToID = new List<ushort>();

            InsertToList();

            DebugConsole.Log("Created linked submarine (" + ID + ")");
        }

        public static LinkedSubmarine CreateDummy(Submarine mainSub, Submarine linkedSub)
        {
            LinkedSubmarine sl = new LinkedSubmarine(mainSub)
            {
                sub = linkedSub
            };

            return sl;
        }
        
        public static LinkedSubmarine CreateDummy(Submarine mainSub, string filePath, Vector2 position)
        {
            XDocument doc = SubmarineInfo.OpenFile(filePath);
            if (doc == null || doc.Root == null) return null;

            LinkedSubmarine sl = CreateDummy(mainSub, doc.Root, position);
            sl.filePath = filePath;
            sl.saveElement = doc.Root;
            sl.saveElement.Name = "LinkedSubmarine";
            sl.saveElement.SetAttributeValue("filepath", filePath);

            return sl;
        }

        public static LinkedSubmarine CreateDummy(Submarine mainSub, XElement element, Vector2 position, ushort id = Entity.NullEntityID)
        {
            LinkedSubmarine sl = new LinkedSubmarine(mainSub, id);
            sl.GenerateWallVertices(element);
            if (sl.wallVertices.Any())
            {
                sl.Rect = new Rectangle(
                    (int)sl.wallVertices.Min(v => v.X + position.X),
                    (int)sl.wallVertices.Max(v => v.Y + position.Y),
                    (int)sl.wallVertices.Max(v => v.X + position.X),
                    (int)sl.wallVertices.Min(v => v.Y + position.Y));

                int width = sl.rect.Width - sl.rect.X;
                int height = sl.rect.Y - sl.rect.Height;
                sl.Rect = new Rectangle((int)(position.X - width / 2), (int)(position.Y + height / 2), width, height);
            }
            else
            {
                sl.Rect = new Rectangle((int)position.X, (int)position.Y, 10, 10);
            }
            return sl;
        }

        public override bool IsMouseOn(Vector2 position)
        {
            return Vector2.Distance(position, WorldPosition) < 50.0f;
        }

        public override MapEntity Clone()
        {
            XElement cloneElement = new XElement(saveElement);
            LinkedSubmarine sl = CreateDummy(Submarine, cloneElement, Position);
            sl.saveElement = cloneElement;
            sl.filePath = filePath;
            return sl;
        }

        private void GenerateWallVertices(XElement rootElement)
        {
            List<Vector2> points = new List<Vector2>();

            var wallPrefabs = StructurePrefab.Prefabs.Where(mp => mp.Body);

            foreach (XElement element in rootElement.Elements())
            {
                if (element.Name != "Structure") { continue; }

                string name = element.GetAttributeString("name", "");
                string identifier = element.GetAttributeString("identifier", "");

                StructurePrefab prefab = Structure.FindPrefab(name, identifier);
                if (prefab == null) { continue; }

                var rect = element.GetAttributeVector4("rect", Vector4.Zero);
                
                points.Add(new Vector2(rect.X, rect.Y));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y));
                points.Add(new Vector2(rect.X, rect.Y - rect.W));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y - rect.W));
            }

            wallVertices = MathUtils.GiftWrap(points);
        }

        // LinkedSubmarine.Load() is called from MapEntity.LoadAll()
        public static LinkedSubmarine Load(XElement element, Submarine submarine, IdRemap idRemap)
        {
            Vector2 pos = element.GetAttributeVector2("pos", Vector2.Zero);
            LinkedSubmarine linkedSub;
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                linkedSub = CreateDummy(submarine, element, pos, idRemap.AssignMaxId());
                linkedSub.saveElement = element;
                linkedSub.purchasedLostShuttles = false;
            }
            else
            {
                string levelSeed = element.GetAttributeString("location", "");
                LevelData levelData = GameMain.GameSession?.Campaign?.NextLevel ?? GameMain.GameSession?.LevelData;
                linkedSub = new LinkedSubmarine(submarine, idRemap.AssignMaxId())
                {
                    purchasedLostShuttles = GameMain.GameSession?.GameMode is CampaignMode campaign && campaign.PurchasedLostShuttles,
                    saveElement = element
                };

                if (!string.IsNullOrWhiteSpace(levelSeed) && levelData != null &&
                    levelData.Seed != levelSeed && !linkedSub.purchasedLostShuttles)
                {
                    linkedSub.loadSub = false;
                }
                else
                {
                    linkedSub.loadSub = true;
                    linkedSub.rect.Location = MathUtils.ToPoint(pos);
                }
            }

            linkedSub.filePath = element.GetAttributeString("filepath", "");
            int[] linkedToIds = element.GetAttributeIntArray("linkedto", new int[0]);
            for (int i = 0; i < linkedToIds.Length; i++)
            {
                linkedSub.linkedToID.Add(idRemap.GetOffsetId(linkedToIds[i]));
            }
            linkedSub.originalLinkedToID = idRemap.GetOffsetId(element.GetAttributeInt("originallinkedto", 0));
            linkedSub.originalMyPortID = (ushort)element.GetAttributeInt("originalmyport", 0);

            return linkedSub.loadSub ? linkedSub : null;
        }

        public void LinkDummyToMainSubmarine()
        {
            if (Screen.Selected != GameMain.SubEditorScreen) { return; }            
            for (int i = 0; i < linkedToID.Count; i++)
            {
                if (FindEntityByID(linkedToID[i]) is MapEntity linked)
                {
                    linkedTo.Add(linked);
                }
            }            
        }

        public override void OnMapLoaded()
        {
            if (!loadSub) { return; }

            SubmarineInfo info = new SubmarineInfo(Submarine.Info.FilePath, "", saveElement);
            if (!info.SubmarineElement.HasElements)
            {
                DebugConsole.ThrowError("Failed to load a linked submarine (empty XML element). The save file may be corrupted.");
                return;
            }
            if (!info.SubmarineElement.Elements().Any(e => e.Name.ToString().Equals("hull", StringComparison.OrdinalIgnoreCase)))
            {
                DebugConsole.ThrowError("Failed to load a linked submarine (the submarine contains no hulls).");
                return;
            }

            IdRemap parentRemap = new IdRemap(Submarine.Info.SubmarineElement, Submarine.IdOffset);
            sub = Submarine.Load(info, false, parentRemap);
            sub.Info.SubmarineClass = Submarine.Info.SubmarineClass;

            IdRemap childRemap = new IdRemap(saveElement, sub.IdOffset);

            Vector2 worldPos = saveElement.GetAttributeVector2("worldpos", Vector2.Zero);
            if (worldPos != Vector2.Zero)
            {
                if (GameMain.GameSession != null && GameMain.GameSession.MirrorLevel)
                {                    
                    worldPos.X = GameMain.GameSession.LevelData.Size.X - worldPos.X;
                }
                sub.SetPosition(worldPos);
            }
            else
            {
                sub.SetPosition(WorldPosition);                
            }

            DockingPort linkedPort = null;
            DockingPort myPort = null;
            
            MapEntity linkedItem = linkedTo.FirstOrDefault(lt => (lt is Item) && ((Item)lt).GetComponent<DockingPort>() != null);
            if (linkedItem == null)
            {
                linkedPort = DockingPort.List.FirstOrDefault(dp => dp.DockingTarget != null && dp.DockingTarget.Item.Submarine == sub);
            }
            else
            {
                linkedPort = ((Item)linkedItem).GetComponent<DockingPort>();
            }

            if (linkedPort == null)
            {
                if (purchasedLostShuttles)
                {
                    linkedPort = (FindEntityByID(originalLinkedToID) as Item)?.GetComponent<DockingPort>();
                }
                if (linkedPort == null) { return; }
            }
            originalLinkedPort = linkedPort;

            ushort originalMyId = childRemap.GetOffsetId(originalMyPortID);
            myPort = (FindEntityByID(originalMyId) as Item)?.GetComponent<DockingPort>();
            if (myPort == null)
            {
                float closestDistance = 0.0f;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (port.Item.Submarine != sub || port.IsHorizontal != linkedPort.IsHorizontal) { continue; }
                    float dist = Vector2.Distance(port.Item.WorldPosition, linkedPort.Item.WorldPosition);
                    if (myPort == null || dist < closestDistance)
                    {
                        myPort = port;
                        closestDistance = dist;
                    }
                }
            }

            if (myPort != null)
            {
                originalMyPortID = myPort.Item.ID;

                myPort.Undock(applyEffects: false);
                myPort.DockingDir = 0;

                //something else is already docked to the port this sub should be docked to
                //may happen if a shuttle is lost, another vehicle docked to where the shuttle used to be,
                //and the shuttle is then restored in the campaign mode
                //or if the user connects multiple subs to the same docking ports in the sub editor
                if (linkedPort.Docked && linkedPort.DockingTarget != null && linkedPort.DockingTarget != myPort)
                {
                    //just spawn below the main sub
                    sub.SetPosition(
                        linkedPort.Item.Submarine.WorldPosition - 
                        new Vector2(0, linkedPort.Item.Submarine.GetDockedBorders().Height / 2 + sub.GetDockedBorders().Height / 2));
                }
                else
                {
                    Vector2 portDiff = myPort.Item.WorldPosition - sub.WorldPosition;
                    Vector2 offset = myPort.IsHorizontal ?
                        Vector2.UnitX * myPort.GetDir(linkedPort) :
                        Vector2.UnitY * myPort.GetDir(linkedPort);
                    offset *= myPort.DockedDistance;

                    sub.SetPosition((linkedPort.Item.WorldPosition - portDiff) - offset);

                    myPort.Dock(linkedPort);
                    myPort.Lock(isNetworkMessage: true, applyEffects: false);
                }
            }

            if (GameMain.GameSession?.GameMode is CampaignMode campaign && campaign.PurchasedLostShuttles)
            {
                foreach (Structure wall in Structure.WallList)
                {
                    if (wall.Submarine != sub) { continue; }
                    for (int i = 0; i < wall.SectionCount; i++)
                    {
                        wall.SetDamage(i, 0, createNetworkEvent: false);
                    }                    
                }
                foreach (Hull hull in Hull.hullList)
                {
                    if (hull.Submarine != sub) { continue; }
                    hull.WaterVolume = 0.0f;
                    hull.OxygenPercentage = 100.0f;
                }
            }

            sub.SetPosition(sub.WorldPosition - Submarine.WorldPosition);
            sub.Submarine = Submarine;
        }

        public override XElement Save(XElement parentElement)
        {
            XElement saveElement = null;

            if (sub == null)
            {
                if (this.saveElement == null)
                {
                    var doc = SubmarineInfo.OpenFile(filePath);
                    saveElement = doc.Root;
                    saveElement.Add(new XAttribute("filepath", filePath));
                }
                else
                {
                    saveElement = this.saveElement;
                }
                saveElement.Name = "LinkedSubmarine";

                if (saveElement.Attribute("previewimage") != null)
                {
                    saveElement.Attribute("previewimage").Remove();
                }

                if (saveElement.Attribute("pos") != null) { saveElement.Attribute("pos").Remove(); }
                saveElement.Add(new XAttribute("pos", XMLExtensions.Vector2ToString(Position - Submarine.HiddenSubPosition)));

                var linkedPort = linkedTo.FirstOrDefault(lt => (lt is Item) && ((Item)lt).GetComponent<DockingPort>() != null);
                if (linkedPort != null)
                {
                    saveElement.Attribute("linkedto")?.Remove();
                    saveElement.Add(new XAttribute("linkedto", linkedPort.ID));
                }
            }
            else
            {
                saveElement = new XElement("LinkedSubmarine");
                sub.SaveToXElement(saveElement);
            }

            saveElement.Attribute("originallinkedto")?.Remove();
            saveElement.Add(new XAttribute("originallinkedto", originalLinkedPort != null ? originalLinkedPort.Item.ID : originalLinkedToID));
            saveElement.Attribute("originalmyport")?.Remove();
            saveElement.Add(new XAttribute("originalmyport", originalMyPortID));

            if (sub != null)
            {
                bool leaveBehind = false;
                if (!sub.DockedTo.Contains(Submarine.MainSub))
                {
                    System.Diagnostics.Debug.Assert(Submarine.MainSub.AtEndExit || Submarine.MainSub.AtStartExit);
                    if (Submarine.MainSub.AtEndExit)
                    {
                        leaveBehind = sub.AtEndExit != Submarine.MainSub.AtEndExit;
                    }
                    else
                    {
                        leaveBehind = sub.AtStartExit != Submarine.MainSub.AtStartExit;
                    }
                }

                if (leaveBehind)
                {
                    saveElement.SetAttributeValue("location", Level.Loaded.Seed);
                    Vector2 position = sub.SubBody.Position;
                    if (Level.Loaded.Mirrored)
                    {
                        position.X = Level.Loaded.Size.X - position.X;
                    }
                    saveElement.SetAttributeValue("worldpos", XMLExtensions.Vector2ToString(position));
                }
                else
                {
                    if (saveElement.Attribute("location") != null) saveElement.Attribute("location").Remove();
                    if (saveElement.Attribute("worldpos") != null) saveElement.Attribute("worldpos").Remove();
                }
                saveElement.SetAttributeValue("pos", XMLExtensions.Vector2ToString(Position - Submarine.HiddenSubPosition));
            }

            parentElement.Add(saveElement);

            return saveElement;
        }
    }
}
