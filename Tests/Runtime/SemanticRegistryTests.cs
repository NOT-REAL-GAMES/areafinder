using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace NotRealGames.Areafinder.Tests
{
    public sealed class SemanticRegistryTests
    {
        private SemanticRegistryAsset _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = ScriptableObject.CreateInstance<SemanticRegistryAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_registry);
        }

        [Test]
        public void TombstoneDoesNotRenumberOrReuseFollowingSlot()
        {
            var ids = new List<SemanticId>();
            for (int slot = 0; slot <= 13; slot++)
            {
                ids.Add(_registry.Add($"Semantic {slot}"));
            }

            Assert.That(_registry.Delete(ids[12]), Is.True);
            SemanticId appended = _registry.Add("Semantic 14");

            Assert.That(_registry.TryGet(ids[12], out SemanticDefinition deleted), Is.True);
            Assert.That(deleted.Slot, Is.EqualTo(12));
            Assert.That(deleted.IsDeleted, Is.True);
            Assert.That(_registry.TryGet(ids[13], out SemanticDefinition following), Is.True);
            Assert.That(following.Slot, Is.EqualTo(13));
            Assert.That(_registry.TryGet(appended, out SemanticDefinition latest), Is.True);
            Assert.That(latest.Slot, Is.EqualTo(14));
            Assert.That(_registry.SlotCapacity, Is.EqualTo(15));
        }

        [Test]
        public void RestorePreservesIdentitySlotAndMaskMeaning()
        {
            SemanticId id = _registry.Add("Walkable");
            _registry.Add("Public");
            var mask = new SemanticMask(_registry.SlotCapacity);
            mask.Set(0);

            Assert.That(_registry.Delete(id), Is.True);
            Assert.That(mask.Contains(0), Is.True);
            Assert.That(_registry.Restore(id), Is.True);
            Assert.That(_registry.TryGet(id, out SemanticDefinition restored), Is.True);
            Assert.That(restored.Id, Is.EqualTo(id));
            Assert.That(restored.Slot, Is.EqualTo(0));
            Assert.That(restored.IsDeleted, Is.False);
            Assert.That(mask.Contains(restored.Slot), Is.True);
        }

        [Test]
        public void RenameChangesPresentationWithoutChangingIdentityOrSlot()
        {
            SemanticId id = _registry.Add("Road", "Old description");
            Assert.That(_registry.TryGet(id, out SemanticDefinition before), Is.True);
            int slot = before.Slot;
            ulong schemaFingerprint = _registry.SchemaFingerprint;

            Assert.That(_registry.Rename(id, "Public road"), Is.True);
            Assert.That(_registry.SetDescription(id, "Open to everyone"), Is.True);

            Assert.That(_registry.TryGet(id, out SemanticDefinition after), Is.True);
            Assert.That(after.Id, Is.EqualTo(id));
            Assert.That(after.Slot, Is.EqualTo(slot));
            Assert.That(after.DisplayName, Is.EqualTo("Public road"));
            Assert.That(after.Description, Is.EqualTo("Open to everyone"));
            Assert.That(_registry.SchemaFingerprint, Is.EqualTo(schemaFingerprint));
        }

        [Test]
        public void CompactionMapRemovesOnlyTombstonesAndPreservesGuids()
        {
            SemanticId first = _registry.Add("First");
            SemanticId deleted = _registry.Add("Deleted");
            SemanticId third = _registry.Add("Third");
            _registry.Delete(deleted);

            int[] map = _registry.BuildCompactionMap();
            _registry.ApplyCompaction(map);

            Assert.That(map, Is.EqualTo(new[] { 0, -1, 1 }));
            Assert.That(_registry.SlotCapacity, Is.EqualTo(2));
            Assert.That(_registry.TryGet(first, out SemanticDefinition firstDefinition), Is.True);
            Assert.That(firstDefinition.Slot, Is.EqualTo(0));
            Assert.That(_registry.TryGet(third, out SemanticDefinition thirdDefinition), Is.True);
            Assert.That(thirdDefinition.Slot, Is.EqualTo(1));
            Assert.That(_registry.TryGet(deleted, out _), Is.False);
        }
    }
}
