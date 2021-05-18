﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenSage.Content;
using OpenSage.Content.Translation;
using OpenSage.Data.Sav;
using OpenSage.Logic.Object;
using OpenSage.Mathematics;
using OpenSage.Utilities.Extensions;

namespace OpenSage.Logic
{
    [DebuggerDisplay("[Player: {Name}]")]
    public class Player
    {
        private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly AssetStore _assetStore;

        private readonly List<Upgrade> _upgrades;
        private readonly StringSet _upgradesInProgress;

        public readonly StringSet UpgradesCompleted;

        private readonly ScienceSet _sciences;
        private readonly ScienceSet _sciencesDisabled;
        private readonly ScienceSet _sciencesHidden;

        private readonly PlayerRelationships _playerToPlayerRelationships = new PlayerRelationships();
        private readonly PlayerRelationships _playerToTeamRelationships = new PlayerRelationships();

        public PlayerTemplate Template { get; }
        public string Name { get; internal set; }
        public string DisplayName { get; private set; }

        public string Side { get; private set; }

        public bool IsHuman { get; private set; }

        public readonly BankAccount BankAccount;

        public Rank Rank { get; set; }
        public uint SkillPointsTotal { get; private set; }
        public uint SkillPointsAvailable { get; set; }
        public uint SciencePurchasePoints { get; set; }
        public bool CanBuildUnits;
        public bool CanBuildBuildings;
        public float GeneralsExperienceMultiplier;
        public bool ShowOnScoreScreen;

        // TODO: Should this be derived from the player's buildings so that it doesn't get out of sync?
        public int GetEnergy(GameObjectCollection allGameObjects)
        {
            var energy = 0;
            foreach (var gameObject in allGameObjects.Items)
            {
                if (gameObject.Owner != this)
                {
                    continue;
                }
                energy += gameObject.EnergyProduction;
            }
            return energy;
        }

        public void LogicTick()
        {
            Rank.Update();
        }

        public bool SpecialPowerAvailable(SpecialPower specialPower)
        {
            if (specialPower.RequiredSciences != null)
            {
                foreach (var requirement in specialPower.RequiredSciences)
                {
                    if (!HasScience(requirement.Value))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public ColorRgb Color { get; }

        private HashSet<Player> _allies;
        public IReadOnlyCollection<Player> Allies => _allies;

        private HashSet<Player> _enemies;
        public IReadOnlyCollection<Player> Enemies => _enemies;

        // TODO: Does the order matter? Is it ever visible in UI?
        // TODO: Yes the order does matter. For example, the sound played when moving mixed groups of units is the one for the most-recently-selected unit.
        private HashSet<GameObject> _selectedUnits;
        public IReadOnlyCollection<GameObject> SelectedUnits => _selectedUnits;

        public GameObject HoveredUnit { get; set; }

        public int Team { get; init; }

        public Player(PlayerTemplate template, in ColorRgb color, AssetStore assetStore)
        {
            Template = template;
            Color = color;
            _selectedUnits = new HashSet<GameObject>();
            _allies = new HashSet<Player>();
            _enemies = new HashSet<Player>();

            _upgrades = new List<Upgrade>();
            _upgradesInProgress = new StringSet();
            UpgradesCompleted = new StringSet();

            _sciences = new ScienceSet(assetStore);
            _sciencesDisabled = new ScienceSet(assetStore);
            _sciencesHidden = new ScienceSet(assetStore);

            _assetStore = assetStore;

            Rank = new Rank(this, assetStore.Ranks);

            if (template?.InitialUpgrades != null)
            {
                foreach (var upgrade in template.InitialUpgrades)
                {
                    AddUpgrade(upgrade.Value, UpgradeStatus.Completed);
                }
            }

            if (template?.IntrinsicSciences != null)
            {
                foreach (var science in template.IntrinsicSciences)
                {
                    _sciences.Add(science.Value.Name, science.Value);
                }
            }

            BankAccount = new BankAccount();
        }

        internal void SelectUnits(IEnumerable<GameObject> units, bool additive = false)
        {
            if (additive)
            {
                _selectedUnits.UnionWith(units);
            }
            else
            {
                _selectedUnits = units.ToSet();
            }

            var unitsFromHordeSelection = new List<GameObject>();
            foreach (var unit in _selectedUnits)
            {
                unit.IsSelected = true;

                if (unit.ParentHorde != null && !unit.ParentHorde.IsSelected)
                {
                    unitsFromHordeSelection.Add(unit.ParentHorde);
                    unitsFromHordeSelection.AddRange(unit.ParentHorde.FindBehavior<HordeContainBehavior>()?.SelectAll(true));
                }
                else
                {
                    var hordeContain = unit.FindBehavior<HordeContainBehavior>();
                    if (hordeContain != null)
                    {
                        unitsFromHordeSelection.AddRange(hordeContain.SelectAll(true));
                    }
                }
            }
            _selectedUnits.UnionWith(unitsFromHordeSelection);
        }

        public void DeselectUnits()
        {
            foreach (var unit in _selectedUnits)
            {
                unit.IsSelected = false;

                if (unit.ParentHorde != null && unit.ParentHorde.IsSelected)
                {
                    unit.ParentHorde.FindBehavior<HordeContainBehavior>()?.SelectAll(false);
                }
                else
                {
                    var hordeContain = unit.FindBehavior<HordeContainBehavior>();
                    if (hordeContain != null)
                    {
                        hordeContain.SelectAll(false);
                    }
                }
            }
            _selectedUnits.Clear();
        }

        public bool ScienceAvailable(Science science)
        {
            if (HasScience(science))
            {
                return false;
            }

            if (_sciencesDisabled.ContainsKey(science.Name))
            {
                return false;
            }

            if (_sciencesHidden.ContainsKey(science.Name))
            {
                return false;
            }

            foreach (var requiredScience in science.PrerequisiteSciences)
            {
                if (requiredScience.Value == null)
                {
                    continue;
                }

                if (!_sciences.ContainsKey(requiredScience.Value.Name))
                {
                    return false;
                }
            }

            return science.SciencePurchasePointCost <= SciencePurchasePoints;
        }

        public void PurchaseScience(Science science)
        {
            if (!ScienceAvailable(science))
            {
                Logger.Warn("Trying to purchase science without fullfilling requirements");
                return;
            }

            if (!science.IsGrantable)
            {
                return;
            }

            SciencePurchasePoints -= (uint) science.SciencePurchasePointCost;
            _sciences.Add(science.Name, science);
        }

        public bool HasScience(Science science)
        {
            return _sciences.ContainsKey(science.Name);
        }

        public bool CanProduceObject(GameObjectCollection allGameObjects, ObjectDefinition objectToProduce)
        {
            if (objectToProduce.Prerequisites == null)
            {
                return true;
            }

            // TODO: Make this more efficient.
            bool HasPrerequisite(ObjectDefinition prerequisite)
            {
                foreach (var gameObject in allGameObjects.Items)
                {
                    if (gameObject.Owner == this && gameObject.Definition == prerequisite)
                    {
                        return true;
                    }
                }

                return false;
            }

            // Prerequisites are AND'd.
            foreach (var prerequisiteList in objectToProduce.Prerequisites.Objects)
            {
                // The list within each prerequisite is OR'd.

                var hasPrerequisite = false;
                foreach (var prerequisite in prerequisiteList)
                {
                    if (HasPrerequisite(prerequisite.Value))
                    {
                        hasPrerequisite = true;
                        break;
                    }
                }

                if (!hasPrerequisite)
                {
                    return false;
                }
            }

            return true;
        }

        internal Upgrade AddUpgrade(UpgradeTemplate template, UpgradeStatus status)
        {
            Upgrade upgrade = null;
            foreach (var eachUpgrade in _upgrades)
            {
                if (eachUpgrade.Template == template)
                {
                    upgrade = eachUpgrade;
                    break;
                }
            }

            if (upgrade == null)
            {
                upgrade = new Upgrade(template);
            }

            upgrade.Status = status;

            _upgrades.Add(upgrade);

            switch (status)
            {
                case UpgradeStatus.Queued:
                    _upgradesInProgress.Add(template.Name);
                    break;

                case UpgradeStatus.Completed:
                    _upgradesInProgress.Remove(template.Name);
                    UpgradesCompleted.Add(template.Name);
                    break;
            }

            return upgrade;
        }

        internal void RemoveUpgrade(UpgradeTemplate template)
        {
            Upgrade upgradeToRemove = null;

            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Template == template)
                {
                    upgradeToRemove = upgrade;
                    break;
                }
            }

            if (upgradeToRemove != null)
            {
                _upgrades.Remove(upgradeToRemove);
            }
        }

        internal bool HasUpgrade(UpgradeTemplate template)
        {
            foreach (var upgrade in _upgrades)
            {
                if (upgrade.Template == template)
                {
                    return true;
                }
            }

            return false;
        }

        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(8);

            BankAccount.Load(reader);

            var upgradeQueueCount = reader.ReadUInt16();

            if (reader.ReadBoolean())
            {
                throw new InvalidDataException();
            }

            _sciencesDisabled.Load(reader);
            _sciencesHidden.Load(reader);

            for (var i = 0; i < upgradeQueueCount; i++)
            {
                var upgradeName = reader.ReadAsciiString();
                var upgradeTemplate = _assetStore.Upgrades.GetByName(upgradeName);

                // Use UpgradeStatus.Invalid temporarily because we're going to load the
                // actual queued / completed status below.
                var upgrade = AddUpgrade(upgradeTemplate, UpgradeStatus.Invalid);

                upgrade.Load(reader);
            }

            reader.__Skip(9);

            var hasInsufficientPower = reader.ReadBoolean();

            _upgradesInProgress.Load(reader);
            UpgradesCompleted.Load(reader);

            if (reader.ReadByte() != 2)
            {
                throw new InvalidDataException();
            }

            var someKindOfPlayerIndex = reader.ReadUInt32();

            reader.__Skip(6);

            var someCount = reader.ReadUInt16();
            for (var i = 0; i < someCount; i++)
            {
                var unknown10 = reader.ReadUInt16();
                if (unknown10 != 2)
                {
                    //throw new InvalidDataException();
                }

                var objectName = reader.ReadAsciiString();
                var position = reader.ReadVector3();

                reader.__Skip(18);

                var maybeHealth = reader.ReadUInt32(); // 100

                reader.__Skip(63);
            }

            var isAIPlayer = reader.ReadBoolean();
            if (isAIPlayer)
            {
                // TODO: There are sometimes floats in here, X and Y and maybe a height.
                reader.__Skip(86);
            }

            reader.ReadBoolean();

            var somePlayerType = reader.ReadBoolean();
            if (somePlayerType)
            {
                var constructedUnits = new ObjectIdSet();
                constructedUnits.Load(reader);

                var constructedBuildings = new ObjectIdSet();
                constructedBuildings.Load(reader);

                reader.__Skip(13);
            }

            var playerID = reader.ReadUInt32();

            _sciences.Load(reader);

            var rankId = reader.ReadUInt32();
            Rank.SetRank((int) rankId);
            SkillPointsTotal = reader.ReadUInt32();
            SkillPointsAvailable = reader.ReadUInt32();

            var unknown4 = reader.ReadUInt32(); // 800
            var unknown5 = reader.ReadUInt32(); // 0

            Name = reader.ReadUnicodeString();

            _playerToPlayerRelationships.Load(reader);
            _playerToTeamRelationships.Load(reader);

            CanBuildUnits = reader.ReadBoolean();
            CanBuildBuildings = reader.ReadBoolean();

            var unknown6 = reader.ReadBoolean();

            GeneralsExperienceMultiplier = reader.ReadSingle();
            ShowOnScoreScreen = reader.ReadBoolean();

            reader.__Skip(87);

            var suppliesCollected = reader.ReadUInt32();
            var moneySpent = reader.ReadUInt32();

            reader.__Skip(156);

            var unknown8 = reader.ReadUInt32();

            var buildingsCreated = new PlayerStatObjectCollection();
            buildingsCreated.Load(reader);

            var numPlayers = reader.ReadUInt16();
            for (var i = 0; i < numPlayers; i++)
            {
                var playerObjectsDestroyed = new PlayerStatObjectCollection();
                playerObjectsDestroyed.Load(reader);
            }

            var unitsCreated = new PlayerStatObjectCollection();
            unitsCreated.Load(reader);

            var buildingsCaptured = new PlayerStatObjectCollection();
            buildingsCaptured.Load(reader);

            var unknown11 = reader.ReadUInt32();
            if (unknown11 != 0)
            {
                throw new InvalidDataException();
            }

            var numControlGroups = reader.ReadUInt16();
            for (var i = 0; i < numControlGroups; i++)
            {
                var controlGroup = new ObjectIdSet();
                controlGroup.Load(reader);
            }

            if (!reader.ReadBoolean())
            {
                throw new InvalidDataException();
            }

            var destroyedObjects = new ObjectIdSet();
            destroyedObjects.Load(reader);

            reader.__Skip(14);
        }

        private static Player FromMapData(Data.Map.Player mapPlayer, AssetStore assetStore)
        {
            var side = mapPlayer.Properties["playerFaction"].Value as string;

            if (side.StartsWith("FactionChina", System.StringComparison.InvariantCultureIgnoreCase))
            {
                // TODO: Probably not right.
                side = "FactionChina";
            }

            // We need the template for default values
            var template = assetStore.PlayerTemplates.GetByName(side);

            var name = mapPlayer.Properties["playerName"].Value as string;
            var displayName = mapPlayer.Properties["playerDisplayName"].Value as string;
            var translatedDisplayName = displayName.Translate();

            var isHuman = (bool) mapPlayer.Properties["playerIsHuman"].Value;

            var colorRgb = mapPlayer.Properties.GetPropOrNull("playerColor")?.Value as uint?;

            ColorRgb color;

            if (colorRgb != null)
            {
                color = ColorRgb.FromUInt32(colorRgb.Value);
            }
            else if (template != null) // Template is null for the neutral faction
            {
                color = template.PreferredColor;
            }
            else
            {
                color = new ColorRgb(0, 0, 0);
            }

            return new Player(template, color, assetStore)
            {
                Side = side,
                Name = name,
                DisplayName = translatedDisplayName,
                IsHuman = isHuman
            };
        }

        // This needs to operate on the entire player list, because players have references to each other
        // (allies and enemies).
        internal static IEnumerable<Player> FromMapData(Data.Map.Player[] mapPlayers, AssetStore assetStore)
        {
            var players = new Dictionary<string, Player>();
            var allies = new Dictionary<string, string[]>();
            var enemies = new Dictionary<string, string[]>();

            foreach (var mapPlayer in mapPlayers)
            {
                var player = FromMapData(mapPlayer, assetStore);
                players[player.Name] = player;
                allies[player.Name] =
                    (mapPlayer.Properties.GetPropOrNull("playerAllies")?.Value as string)?.Split(' ');
                enemies[player.Name] =
                    (mapPlayer.Properties.GetPropOrNull("playerEnemies")?.Value as string)?.Split(' ');
            }

            foreach (var (name, player) in players)
            {
                player._allies = allies[name].Select(ally => players[ally]).ToSet();
                player._enemies = enemies[name].Select(enemy => players[enemy]).ToSet();
            }

            return players.Values;
        }

        public static Player FromTemplate(GameData gameData, PlayerTemplate template, AssetStore assetStore, PlayerSetting? setting = null)
        {
            var color = setting.HasValue ? setting.Value.Color : template.PreferredColor;

            // TODO: Use rest of the properties from the template
            var result = new Player(template, color, assetStore)
            {
                Side = template.Side,
                Name = setting == null ? template.Name : setting?.Name,
                DisplayName = template.DisplayName.Translate(),
                IsHuman = setting?.Owner == PlayerOwner.Player,
                Team = setting?.Team ?? default,
            };

            result.BankAccount.Money = (uint) (template.StartMoney + gameData.DefaultStartingCash);

            return result;
        }

        public void AddAlly(Player player)
        {
            _allies.Add(player);
        }

        public void AddEnemy(Player player)
        {
            _enemies.Add(player);
        }
    }

    public enum RelationshipType : uint
    {
        Enemies = 0,
        Neutral = 1,
        Allies = 2,
    }

    internal sealed class PlayerRelationships
    {
        private readonly Dictionary<uint, RelationshipType> _store;

        public PlayerRelationships()
        {
            _store = new Dictionary<uint, RelationshipType>();
        }

        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            _store.Clear();

            var count = reader.ReadUInt16();
            for (var i = 0; i < count; i++)
            {
                var playerOrTeamId = reader.ReadUInt32();
                var relationship = reader.ReadEnum<RelationshipType>();
                _store[playerOrTeamId] = relationship;
            }
        }
    }

    public sealed class ScienceSet : Dictionary<string, Science>
    {
        private readonly AssetStore _assetStore;

        internal ScienceSet(AssetStore assetStore)
        {
            _assetStore = assetStore;
        }

        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            Clear();

            var count = reader.ReadUInt16();
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadAsciiString();

                var science = _assetStore.Sciences.GetByName(name);

                Add(name, science);
            }
        }
    }

    // TODO: I don't know if these are always serialized the same way in .sav files.
    // Maybe we shouldn't use a generic container like this.
    public sealed class StringSet : HashSet<string>
    {
        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            Clear();

            var count = reader.ReadUInt16();
            for (var i = 0; i < count; i++)
            {
                Add(reader.ReadAsciiString());
            }
        }
    }

    // TODO: I don't know if these are always serialized the same way in .sav files.
    // Maybe we shouldn't use a generic container like this.
    public sealed class ObjectIdSet : HashSet<uint>
    {
        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            Clear();

            var count = reader.ReadUInt16();
            for (var i = 0; i < count; i++)
            {
                Add(reader.ReadUInt32());
            }
        }
    }

    internal sealed class PlayerStats
    {
        public readonly PlayerStatObjectCollection UnitsDestroyed = new PlayerStatObjectCollection();

        internal void Load(SaveFileReader reader)
        {
            // After 0x10, 3rd entry is ObjectsDestroyed?
            // After 0x10, 17th entry is ObjectsLost?
            UnitsDestroyed.Load(reader);
        }
    }

    internal sealed class PlayerStatObjectCollection : Dictionary<string, uint>
    {
        internal void Load(SaveFileReader reader)
        {
            Clear();

            reader.ReadVersion(1);

            var count = reader.ReadUInt16();
            for (var i = 0; i < count; i++)
            {
                var objectType = reader.ReadAsciiString();
                var total = reader.ReadUInt32();

                Add(objectType, total);
            }
        }
    }

    public enum UpgradeStatus
    {
        Invalid = 0,
        Queued = 1,
        Completed = 2
    }

    public sealed class BankAccount
    {
        private static NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public uint Money { get; internal set; }

        public void Withdraw(uint amount)
        {
            // TODO: Play MoneyWithdrawSound

            if (Money >= amount)
            {
                Money -= amount;
            }
            else
            {
                // this should not happen since we should check first if we can spend that much
                Logger.Warn($"Spent more money ({amount}) than player had ({Money})!");
                Money = 0;
            }
        }

        public void Deposit(uint amount)
        {
            // TODO: Play MoneyDepositSound

            Money += amount;
        }

        internal void Load(SaveFileReader reader)
        {
            reader.ReadVersion(1);

            Money = reader.ReadUInt32();
        }
    }
}
