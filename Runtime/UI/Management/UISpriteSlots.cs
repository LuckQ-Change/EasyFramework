using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    internal sealed class UISpriteSlots
    {
        private sealed class Slot
        {
            internal Sprite Original;
            internal Sprite Assigned;
            internal AssetLease<Sprite> Lease;
            internal CancellationTokenSource Pending;
            internal bool HasAssignment;
        }

        private readonly Dictionary<Image, Slot> _slots = new();

        internal async Task<bool> SetAsync(Image image, string address, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!_slots.TryGetValue(image, out Slot slot))
                _slots.Add(image, slot = new Slot { Original = image.sprite });

            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationTokenSource previous = slot.Pending;
            slot.Pending = request;
            previous?.Cancel();
            AssetLease<Sprite> lease = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(address))
                    lease = await UIManager.RequireAssets().AcquireAsync<Sprite>(address, request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (image == null) return false;

                slot.Assigned = lease?.Asset;
                image.sprite = slot.Assigned;
                slot.HasAssignment = true;
                AssetLease<Sprite> old = slot.Lease;
                slot.Lease = lease;
                lease = null;
                old?.Dispose();
                return true;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested && !token.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                if (ReferenceEquals(slot.Pending, request)) slot.Pending = null;
                lease?.Dispose();
            }
        }

        internal void CancelPending()
        {
            foreach (Slot slot in _slots.Values) slot.Pending?.Cancel();
        }

        internal void Release()
        {
            CancelPending();
            foreach (var pair in _slots)
            {
                var image = pair.Key;
                var slot = pair.Value;
                if (image != null && slot.HasAssignment && image.sprite == slot.Assigned) image.sprite = slot.Original;
                slot.Lease?.Dispose();
            }
            _slots.Clear();
        }

        internal void AddUsage(ref int leases, ref long estimatedBytes)
        {
            foreach (Slot slot in _slots.Values)
            {
                if (slot.Lease?.IsValid != true) continue;
                leases++;
                estimatedBytes += slot.Lease.Entry.Bytes;
            }
        }
    }
}
