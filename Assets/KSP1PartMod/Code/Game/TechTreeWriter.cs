using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSP.Game.Science;
using KSP1PartMod.Placement;
using UnityEngine;

namespace KSP1PartMod.Game
{
    /// <summary>The tech tree as the writer sees it. Positions are TechNodeData.TechTreePosition.</summary>
    public interface ITechTreeStore
    {
        bool HasNode(string id);
        (float x, float y) Position(string id);
        IReadOnlyCollection<(float x, float y)> AllPositions();
        /// <summary>Cost, icon, category, tier and version are taken from anchor.</summary>
        void AddNode(string id, string anchor, (float x, float y) position, string[] requires, string nameKey, string descriptionKey);
        void RemoveNode(string id);
        void AddPart(string nodeId, string part);
        void RemovePart(string nodeId, string part);
    }

    /// <summary>Writes a PlacementPlan into a store and remembers what it added, so it can take it back.</summary>
    public sealed class TechTreeWriter
    {
        public const string NameKeyPrefix = "KSP1PartMod/TechNodes/Names/";
        public const string DescriptionKeyPrefix = "KSP1PartMod/TechNodes/Descriptions/";

        private readonly ITechTreeStore _store;
        private readonly List<string> _addedNodes = new List<string>();
        private readonly List<(string node, string part)> _addedParts = new List<(string, string)>();
        private readonly List<string> _problems = new List<string>();

        public TechTreeWriter(ITechTreeStore store) => _store = store ?? throw new ArgumentNullException(nameof(store));

        /// <summary>What the last Apply could not write (a missing anchor or node), as log lines.</summary>
        public IReadOnlyList<string> Problems => _problems;

        /// <summary>Reverts the previous Apply, then creates the plan's new nodes and adds its parts. A new node
        /// already in the store is left as it is (neither created nor removed later); its parts are still added.</summary>
        public void Apply(PlacementPlan plan)
        {
            Revert();
            _problems.Clear();

            var taken = _store.AllPositions().ToList();
            foreach (var spec in plan.NodesToCreate)
            {
                if (_store.HasNode(spec.NodeId)) continue;
                if (!_store.HasNode(spec.Anchor))
                {
                    _problems.Add($"[KSP1PartMod] node {spec.NodeId} not created: anchor {spec.Anchor} is not in the tree");
                    continue;
                }
                var position = NodeLayout.PlaceNewNode(_store.Position(spec.Anchor), (spec.OffsetX, spec.OffsetY), taken);
                // A required node may have been skipped above: require the anchor instead.
                var requires = spec.Requires.Select(r => _store.HasNode(r) ? r : spec.Anchor).Distinct().ToArray();
                _store.AddNode(spec.NodeId, spec.Anchor, position, requires,
                    NameKeyPrefix + spec.NodeId, DescriptionKeyPrefix + spec.NodeId);
                _addedNodes.Add(spec.NodeId);
                taken.Add(position);
            }

            foreach (var pair in plan.PartsByNode)
            {
                if (!_store.HasNode(pair.Key))
                {
                    _problems.Add($"[KSP1PartMod] {pair.Value.Count} part(s) not placed: node {pair.Key} is not in the tree " +
                                  $"({string.Join(", ", pair.Value)})");
                    continue;
                }
                foreach (var part in pair.Value)
                {
                    _store.AddPart(pair.Key, part);
                    _addedParts.Add((pair.Key, part));
                }
            }
        }

        /// <summary>Removes what this writer added, newest first. Entries it did not add are untouched.</summary>
        public void Revert()
        {
            for (var i = _addedParts.Count - 1; i >= 0; i--) _store.RemovePart(_addedParts[i].node, _addedParts[i].part);
            for (var i = _addedNodes.Count - 1; i >= 0; i--) _store.RemoveNode(_addedNodes[i]);
            _addedParts.Clear();
            _addedNodes.Clear();
        }
    }

    /// <summary>ITechTreeStore over the game's TechNodeDataStore, by reflection on its private dictionaries.
    /// CacheTechNodeRelationshipData is not re-run: it appends to _alwaysUnlockedTechNodes without clearing it and
    /// logs an error for every part already in _partIDLookup (IL, 2026-10-25). The lookups are kept in step here.</summary>
    internal sealed class GameTechTreeStore : ITechTreeStore
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly Dictionary<string, TechNodeData> _nodes;
        private readonly Dictionary<string, string> _partLookup;

        public GameTechTreeStore(TechNodeDataStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            _nodes = (Dictionary<string, TechNodeData>)Field(store, "_availableData");
            _partLookup = (Dictionary<string, string>)Field(store, "_partIDLookup");
        }

        private static object Field(TechNodeDataStore store, string name) =>
            (typeof(TechNodeDataStore).GetField(name, Private)
             ?? throw new MissingFieldException(nameof(TechNodeDataStore), name)).GetValue(store)
            ?? throw new InvalidOperationException($"TechNodeDataStore.{name} is null");

        public bool HasNode(string id) => id != null && _nodes.ContainsKey(id);

        public (float x, float y) Position(string id)
        {
            var p = _nodes[id].TechTreePosition;
            return (p.x, p.y);
        }

        public IReadOnlyCollection<(float x, float y)> AllPositions() =>
            _nodes.Values.Select(n => (n.TechTreePosition.x, n.TechTreePosition.y)).ToList();

        public void AddNode(string id, string anchor, (float x, float y) position, string[] requires, string nameKey,
            string descriptionKey)
        {
            var a = _nodes[anchor];
            _nodes[id] = new TechNodeData
            {
                Version = a.Version,
                ID = id,
                NameLocKey = nameKey,
                IconID = a.IconID,
                CategoryID = a.CategoryID,
                HiddenByNodeID = null,
                DescriptionLocKey = descriptionKey,
                RequiredSciencePoints = a.RequiredSciencePoints,
                UnlockedPartsIDs = Array.Empty<string>(),
                UnlockedVariants = Array.Empty<TechUnlockedVariant>(),
                RequiredTechNodeIDs = requires ?? Array.Empty<string>(),
                RequiredMissionIDs = Array.Empty<string>(),
                RequiredResearchData = Array.Empty<TechRequiredResearchData>(),
                TierToUnlock = a.TierToUnlock,
                TechTreePosition = new Vector2(position.x, position.y),
                MysteryTech = false,
            };
        }

        public void RemoveNode(string id)
        {
            if (id == null || !_nodes.TryGetValue(id, out var node)) return;
            foreach (var part in node.UnlockedPartsIDs ?? Array.Empty<string>())
                if (_partLookup.TryGetValue(part, out var holder) && holder == id) _partLookup.Remove(part);
            _nodes.Remove(id);
        }

        public void AddPart(string nodeId, string part)
        {
            var node = _nodes[nodeId];
            var parts = node.UnlockedPartsIDs ?? Array.Empty<string>();
            if (!parts.Contains(part))
            {
                var longer = new string[parts.Length + 1];
                parts.CopyTo(longer, 0);
                longer[parts.Length] = part;
                node.UnlockedPartsIDs = longer;
            }
            _partLookup[part] = nodeId;
        }

        public void RemovePart(string nodeId, string part)
        {
            if (nodeId == null || !_nodes.TryGetValue(nodeId, out var node)) return;
            if (node.UnlockedPartsIDs != null) node.UnlockedPartsIDs = node.UnlockedPartsIDs.Where(p => p != part).ToArray();
            if (_partLookup.TryGetValue(part, out var holder) && holder == nodeId) _partLookup.Remove(part);
        }
    }
}
